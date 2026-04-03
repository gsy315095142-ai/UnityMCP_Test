#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMCP.Core;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace UnityMCP.Bridge
{
    /// <summary>
    /// Lightweight local HTTP bridge for external clients (LumiClaw MVP).
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityBridgeServer
    {
        private const int DefaultPort = 6847;
        private const string Host = "127.0.0.1";
        private const string AutoStartPrefKey = "UnityMCP.Bridge.AutoStart";
        private const string PortPrefKey = "UnityMCP.Bridge.Port";
        private const string TokenEnabledPrefKey = "UnityMCP.Bridge.TokenEnabled";
        private const string TokenPrefKey = "UnityMCP.Bridge.Token";
        private const string DangerConfirmPrefKey = "UnityMCP.Bridge.RequireDangerConfirm";
        private const int MaxRequestBodyBytes = 1024 * 1024; // 1MB
        private const int ToolCallRateLimitWindowSeconds = 10;
        private const int ToolCallRateLimitMaxRequests = 20;
        private const int MaxRecentDiagnosticsItems = 30;

        private static readonly object Gate = new();
        private static readonly object MetricsGate = new();
        private static readonly Queue<DateTime> ToolCallTimestampsUtc = new();
        private static readonly Queue<string> RecentErrors = new();
        private static readonly Queue<string> RecentCalls = new();
        private static HttpListener? _listener;
        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static long _totalRequests;
        private static long _totalToolCalls;
        private static long _totalRejected;

        static UnityBridgeServer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            // Defer prefs access to editor main loop.
            EditorApplication.delayCall += () =>
            {
                var autoStart = GetPrefBool(AutoStartPrefKey, true);
                EnsureTokenInitialized();
                if (autoStart)
                    Start();
            };
        }

        [MenuItem("Window/AI 助手/Unity Bridge/启动", priority = 210)]
        private static void MenuStart() => Start();

        [MenuItem("Window/AI 助手/Unity Bridge/停止", priority = 211)]
        private static void MenuStop() => Stop();

        [MenuItem("Window/AI 助手/Unity Bridge/复制地址", priority = 212)]
        private static void MenuCopyAddress()
        {
            EditorGUIUtility.systemCopyBuffer = GetBaseUrl();
            Debug.Log("[UnityBridge] 已复制地址: " + GetBaseUrl());
        }

        [MenuItem("Window/AI 助手/Unity Bridge/自动启动", priority = 220)]
        private static void ToggleAutoStart()
        {
            var value = !GetPrefBool(AutoStartPrefKey, true);
            SetPrefBool(AutoStartPrefKey, value);
        }

        [MenuItem("Window/AI 助手/Unity Bridge/启用 Token 鉴权", priority = 221)]
        private static void ToggleTokenAuth()
        {
            var enabled = !GetPrefBool(TokenEnabledPrefKey, false);
            SetPrefBool(TokenEnabledPrefKey, enabled);
            EnsureTokenInitialized();
            Debug.Log(enabled
                ? "[UnityBridge] Token 鉴权已启用"
                : "[UnityBridge] Token 鉴权已关闭");
        }

        [MenuItem("Window/AI 助手/Unity Bridge/复制 Token", priority = 222)]
        private static void MenuCopyToken()
        {
            EnsureTokenInitialized();
            EditorGUIUtility.systemCopyBuffer = GetToken();
            Debug.Log("[UnityBridge] Token 已复制到剪贴板。");
        }

        [MenuItem("Window/AI 助手/Unity Bridge/重新生成 Token", priority = 223)]
        private static void MenuRegenerateToken()
        {
            var token = GenerateToken();
            SetPrefString(TokenPrefKey, token);
            EditorGUIUtility.systemCopyBuffer = token;
            Debug.Log("[UnityBridge] Token 已重新生成并复制。");
        }

        [MenuItem("Window/AI 助手/Unity Bridge/高风险操作需确认", priority = 224)]
        private static void ToggleDangerConfirm()
        {
            var enabled = !GetPrefBool(DangerConfirmPrefKey, true);
            SetPrefBool(DangerConfirmPrefKey, enabled);
            Debug.Log(enabled
                ? "[UnityBridge] 高风险操作确认已启用"
                : "[UnityBridge] 高风险操作确认已关闭");
        }

        [MenuItem("Window/AI 助手/Unity Bridge/自动启动", true)]
        private static bool ValidateToggleAutoStart()
        {
            Menu.SetChecked("Window/AI 助手/Unity Bridge/自动启动", GetPrefBool(AutoStartPrefKey, true));
            return true;
        }

        [MenuItem("Window/AI 助手/Unity Bridge/启用 Token 鉴权", true)]
        private static bool ValidateToggleTokenAuth()
        {
            Menu.SetChecked("Window/AI 助手/Unity Bridge/启用 Token 鉴权", GetPrefBool(TokenEnabledPrefKey, false));
            return true;
        }

        [MenuItem("Window/AI 助手/Unity Bridge/高风险操作需确认", true)]
        private static bool ValidateToggleDangerConfirm()
        {
            Menu.SetChecked("Window/AI 助手/Unity Bridge/高风险操作需确认", GetPrefBool(DangerConfirmPrefKey, true));
            return true;
        }

        [MenuItem("Window/AI 助手/Unity Bridge/启动", true)]
        private static bool ValidateStart()
        {
            return !IsRunning;
        }

        [MenuItem("Window/AI 助手/Unity Bridge/停止", true)]
        private static bool ValidateStop()
        {
            return IsRunning;
        }

        internal static bool IsRunning
        {
            get
            {
                lock (Gate)
                {
                    return _listener is { IsListening: true };
                }
            }
        }

        internal static string GetBaseUrl()
        {
            var port = GetPrefInt(PortPrefKey, DefaultPort);
            if (port <= 0) port = DefaultPort;
            return $"http://{Host}:{port}";
        }

        internal static void Start()
        {
            lock (Gate)
            {
                if (_listener is { IsListening: true })
                    return;

                try
                {
                    var port = GetPrefInt(PortPrefKey, DefaultPort);
                    if (port <= 0) port = DefaultPort;

                    var prefix = $"http://{Host}:{port}/";
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(prefix);
                    _listener.Start();

                    _cts = new CancellationTokenSource();
                    _loopTask = Task.Run(() => AcceptLoop(_listener, _cts.Token), _cts.Token);

                    Debug.Log("[UnityBridge] Started at " + prefix);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[UnityBridge] Start failed: " + ex.Message);
                    CleanupNoLock();
                }
            }
        }

        internal static void Stop()
        {
            lock (Gate)
            {
                CleanupNoLock();
            }
        }

        private static void CleanupNoLock()
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _listener?.Stop(); } catch { /* ignore */ }
            try { _listener?.Close(); } catch { /* ignore */ }
            _cts = null;
            _listener = null;
            _loopTask = null;
        }

        private static async Task AcceptLoop(HttpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested && listener.IsListening)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = await listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(ctx), token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[UnityBridge] Accept failed: " + ex.Message);
                    await Task.Delay(100, token).ContinueWith(_ => { }, CancellationToken.None);
                }
            }
        }

        private static void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                var path = req.Url?.AbsolutePath ?? "/";
                var method = req.HttpMethod?.ToUpperInvariant() ?? "GET";
                lock (MetricsGate)
                {
                    _totalRequests++;
                }

                if (method == "OPTIONS")
                {
                    WriteNoContent(ctx);
                    return;
                }

                if (RequiresTokenAuth(path) && !HasValidToken(ctx.Request))
                {
                    WriteError(ctx, 401, "UNAUTHORIZED", "missing or invalid X-Unity-Bridge-Token", "");
                    return;
                }

                if (method == "GET" && path == "/health")
                {
                    HandleHealth(ctx);
                    return;
                }

                if (method == "GET" && path == "/unity/info")
                {
                    HandleInfo(ctx);
                    return;
                }

                if (method == "GET" && path == "/unity/tools")
                {
                    HandleTools(ctx);
                    return;
                }

                if (method == "GET" && path == "/unity/openapi-lite")
                {
                    HandleOpenApiLite(ctx);
                    return;
                }

                if (method == "GET" && path == "/unity/diagnostics")
                {
                    HandleDiagnostics(ctx);
                    return;
                }

                if (method == "POST" && path == "/unity/tools/call")
                {
                    HandleToolCall(ctx);
                    return;
                }

                WriteError(ctx, 404, "NOT_FOUND", $"Unknown route: {method} {path}", "");
            }
            catch (Exception ex)
            {
                WriteError(ctx, 500, "EXECUTION_FAILED", ex.Message, "");
            }
        }

        private static void HandleHealth(HttpListenerContext ctx)
        {
            var dataJson =
                "{"
                + "\"bridgeStatus\":\"up\","
                + "\"unityStatus\":\"connected\","
                + "\"mode\":\"bridge\","
                + "\"isPlaying\":" + (MainThread.Run(() => EditorApplication.isPlaying) ? "true" : "false")
                + "}";
            WriteOk(ctx, dataJson, "");
        }

        private static void HandleInfo(HttpListenerContext ctx)
        {
            var requestId = EnsureRequestId(ctx.Request.Headers["X-Request-Id"]);
            var infoJson = MainThread.Run(() =>
            {
                var tools = UnityBridgeDispatcher.GetToolDescriptors();
                var available = 0;
                foreach (var t in tools)
                    if (t.Available) available++;

                var projectName = Path.GetFileName(Directory.GetParent(Application.dataPath)?.FullName ?? "");
                return "{"
                       + "\"bridgeVersion\":\"0.1.0\","
                       + "\"unityVersion\":" + JsonEscaper.Q(Application.unityVersion) + ","
                       + "\"projectName\":" + JsonEscaper.Q(projectName) + ","
                       + "\"availableToolCount\":" + available + ","
                       + "\"baseUrl\":" + JsonEscaper.Q(GetBaseUrl()) + ","
                       + "\"tokenAuthEnabled\":" + (GetPrefBool(TokenEnabledPrefKey, false) ? "true" : "false") + ","
                       + "\"mode\":\"bridge\""
                       + "}";
            });

            WriteOk(ctx, infoJson, requestId);
        }

        private static void HandleTools(HttpListenerContext ctx)
        {
            var requestId = EnsureRequestId(ctx.Request.Headers["X-Request-Id"]);
            var toolsJson = MainThread.Run(() =>
            {
                var tools = UnityBridgeDispatcher.GetToolDescriptors();
                var sb = new StringBuilder();
                sb.Append("{\"tools\":[");
                for (var i = 0; i < tools.Count; i++)
                {
                    var t = tools[i];
                    if (i > 0) sb.Append(",");
                    sb.Append("{");
                    sb.Append("\"name\":").Append(JsonEscaper.Q(t.Name)).Append(",");
                    sb.Append("\"description\":").Append(JsonEscaper.Q(t.Description)).Append(",");
                    sb.Append("\"available\":").Append(t.Available ? "true" : "false").Append(",");
                    sb.Append("\"note\":").Append(JsonEscaper.Q(t.Note));
                    sb.Append("}");
                }
                sb.Append("]}");
                return sb.ToString();
            });

            WriteOk(ctx, toolsJson, requestId);
        }

        private static void HandleToolCall(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var contentLength = req.ContentLength64;
            if (contentLength > MaxRequestBodyBytes)
            {
                RecordRejected("PAYLOAD_TOO_LARGE", "content-length over limit");
                WriteError(ctx, 413, "PAYLOAD_TOO_LARGE", $"request body too large (>{MaxRequestBodyBytes} bytes)", "");
                return;
            }

            if (!TryConsumeToolCallQuota(out var retryAfterSeconds))
            {
                RecordRejected("RATE_LIMITED", "tool-call quota exceeded");
                WriteError(
                    ctx,
                    429,
                    "RATE_LIMITED",
                    $"tool call rate limited, retry after {retryAfterSeconds}s",
                    "");
                return;
            }

            var body = ReadBody(req);
            if (string.IsNullOrWhiteSpace(body))
            {
                WriteError(ctx, 400, "BAD_REQUEST", "empty request body", "");
                return;
            }
            if (Encoding.UTF8.GetByteCount(body) > MaxRequestBodyBytes)
            {
                RecordRejected("PAYLOAD_TOO_LARGE", "body bytes over limit");
                WriteError(ctx, 413, "PAYLOAD_TOO_LARGE", $"request body too large (>{MaxRequestBodyBytes} bytes)", "");
                return;
            }

            var requestId = EnsureRequestId(JsonFieldReader.ExtractString(body, "requestId"));
            if (string.IsNullOrWhiteSpace(requestId))
                requestId = EnsureRequestId(req.Headers["X-Request-Id"]);

            var tool = JsonFieldReader.ExtractString(body, "tool");
            var argsJson = JsonFieldReader.ExtractFieldAsJson(body, "arguments");
            if (string.IsNullOrWhiteSpace(argsJson))
                argsJson = "{}";

            var canonicalTool = UnityBridgeDispatcher.CanonicalizeForApi(tool);
            var confirmed = JsonFieldReader.ExtractBool(body, "confirm")
                            || JsonFieldReader.ExtractBool(argsJson, "confirm");
            Debug.Log($"[UnityBridge] requestId={requestId} tool={tool} canonicalTool={canonicalTool} received");
            if (GetPrefBool(DangerConfirmPrefKey, true)
                && UnityBridgeDispatcher.IsDangerousToolCall(canonicalTool, argsJson, out var dangerReason)
                && !confirmed)
            {
                RecordToolCall(canonicalTool, false, 0, requestId, "CONFIRM_REQUIRED");
                WriteError(
                    ctx,
                    409,
                    "CONFIRM_REQUIRED",
                    $"高风险操作需要确认：{dangerReason}。请在请求体中设置 confirm=true。",
                    requestId);
                return;
            }

            var sw = Stopwatch.StartNew();
            var dispatch = UnityBridgeDispatcher.Dispatch(tool, argsJson);
            sw.Stop();
            RecordToolCall(canonicalTool, dispatch.Success, sw.ElapsedMilliseconds, requestId, dispatch.ErrorCode);

            if (!dispatch.Success)
            {
                var status = dispatch.ErrorCode switch
                {
                    "BAD_REQUEST" => 400,
                    "UNAUTHORIZED" => 401,
                    "TOOL_NOT_AVAILABLE" => 409,
                    "CONFIRM_REQUIRED" => 409,
                    "UNITY_NOT_RUNNING" => 503,
                    "TIMEOUT" => 504,
                    "RATE_LIMITED" => 429,
                    "PAYLOAD_TOO_LARGE" => 413,
                    _ => 500
                };
                WriteError(ctx, status, dispatch.ErrorCode, dispatch.Message, requestId);
                Debug.LogWarning($"[UnityBridge] requestId={requestId} tool={canonicalTool} failed code={dispatch.ErrorCode} msg={dispatch.Message}");
                return;
            }

            var dataJson = "{"
                           + "\"tool\":" + JsonEscaper.Q(tool) + ","
                           + "\"canonicalTool\":" + JsonEscaper.Q(canonicalTool) + ","
                           + "\"result\":" + dispatch.DataJson
                           + "}";
            WriteOk(ctx, dataJson, requestId);
            Debug.Log($"[UnityBridge] requestId={requestId} tool={canonicalTool} success elapsedMs={sw.ElapsedMilliseconds}");
        }

        private static string ReadBody(HttpListenerRequest req)
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static void WriteOk(HttpListenerContext ctx, string dataJson, string requestId)
        {
            requestId = EnsureRequestId(requestId);
            ctx.Response.Headers["X-Request-Id"] = requestId;
            var json = "{"
                       + "\"ok\":true,"
                       + "\"data\":" + (string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson) + ","
                       + "\"error\":null,"
                       + "\"requestId\":" + JsonEscaper.Q(requestId ?? "") + ","
                       + "\"timestamp\":" + JsonEscaper.Q(DateTime.UtcNow.ToString("o"))
                       + "}";
            WriteJson(ctx, 200, json);
        }

        private static void WriteError(HttpListenerContext ctx, int status, string code, string message, string requestId)
        {
            requestId = EnsureRequestId(requestId);
            RecordError(code, message, requestId);
            ctx.Response.Headers["X-Request-Id"] = requestId;
            var json = "{"
                       + "\"ok\":false,"
                       + "\"data\":null,"
                       + "\"error\":{"
                       + "\"code\":" + JsonEscaper.Q(code) + ","
                       + "\"message\":" + JsonEscaper.Q(message) + ","
                       + "\"details\":{}"
                       + "},"
                       + "\"requestId\":" + JsonEscaper.Q(requestId ?? "") + ","
                       + "\"timestamp\":" + JsonEscaper.Q(DateTime.UtcNow.ToString("o"))
                       + "}";
            WriteJson(ctx, status, json);
        }

        private static void WriteJson(HttpListenerContext ctx, int statusCode, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var resp = ctx.Response;
            resp.StatusCode = statusCode;
            resp.ContentType = "application/json; charset=utf-8";
            resp.ContentEncoding = Encoding.UTF8;
            resp.ContentLength64 = bytes.Length;
            resp.Headers["Access-Control-Allow-Origin"] = "*";
            resp.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
            resp.Headers["Access-Control-Allow-Headers"] = "Content-Type,X-Request-Id,X-Unity-Bridge-Token";

            try
            {
                using var os = resp.OutputStream;
                os.Write(bytes, 0, bytes.Length);
            }
            catch
            {
                // ignored (client may disconnect early)
            }
        }

        private static void WriteNoContent(HttpListenerContext ctx)
        {
            var resp = ctx.Response;
            resp.StatusCode = 204;
            resp.Headers["Access-Control-Allow-Origin"] = "*";
            resp.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
            resp.Headers["Access-Control-Allow-Headers"] = "Content-Type,X-Request-Id,X-Unity-Bridge-Token";
            try { resp.Close(); } catch { /* ignore */ }
        }

        private static void HandleOpenApiLite(HttpListenerContext ctx)
        {
            var requestId = EnsureRequestId(ctx.Request.Headers["X-Request-Id"]);
            var schemaJson = MainThread.Run(() =>
            {
                var tools = UnityBridgeDispatcher.GetToolDescriptors();
                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"name\":\"unity-bridge\",");
                sb.Append("\"version\":\"0.1.1\",");
                sb.Append("\"baseUrl\":").Append(JsonEscaper.Q(GetBaseUrl())).Append(",");
                sb.Append("\"tokenAuthEnabled\":").Append(GetPrefBool(TokenEnabledPrefKey, false) ? "true" : "false").Append(",");
                sb.Append("\"dangerConfirmEnabled\":").Append(GetPrefBool(DangerConfirmPrefKey, true) ? "true" : "false").Append(",");
                sb.Append("\"routes\":[");
                sb.Append("{\"method\":\"GET\",\"path\":\"/health\"},");
                sb.Append("{\"method\":\"GET\",\"path\":\"/unity/info\"},");
                sb.Append("{\"method\":\"GET\",\"path\":\"/unity/tools\"},");
                sb.Append("{\"method\":\"GET\",\"path\":\"/unity/openapi-lite\"},");
                sb.Append("{\"method\":\"GET\",\"path\":\"/unity/diagnostics\"},");
                sb.Append("{\"method\":\"POST\",\"path\":\"/unity/tools/call\"}");
                sb.Append("],");
                sb.Append("\"limits\":{");
                sb.Append("\"maxRequestBodyBytes\":").Append(MaxRequestBodyBytes).Append(",");
                sb.Append("\"toolCallRateLimit\":{");
                sb.Append("\"windowSeconds\":").Append(ToolCallRateLimitWindowSeconds).Append(",");
                sb.Append("\"maxRequests\":").Append(ToolCallRateLimitMaxRequests);
                sb.Append("}");
                sb.Append("},");
                sb.Append("\"tools\":[");
                for (var i = 0; i < tools.Count; i++)
                {
                    var t = tools[i];
                    if (i > 0) sb.Append(",");
                    sb.Append("{");
                    sb.Append("\"name\":").Append(JsonEscaper.Q(t.Name)).Append(",");
                    sb.Append("\"description\":").Append(JsonEscaper.Q(t.Description)).Append(",");
                    sb.Append("\"available\":").Append(t.Available ? "true" : "false").Append(",");
                    sb.Append("\"note\":").Append(JsonEscaper.Q(t.Note));
                    sb.Append("}");
                }
                sb.Append("],");
                sb.Append("\"toolCallContract\":{");
                sb.Append("\"path\":\"/unity/tools/call\",");
                sb.Append("\"requestExample\":{");
                sb.Append("\"tool\":\"get_scene_state\",");
                sb.Append("\"arguments\":{},");
                sb.Append("\"requestId\":\"<uuid>\",");
                sb.Append("\"confirm\":false");
                sb.Append("}");
                sb.Append("}");
                sb.Append("}");
                return sb.ToString();
            });

            WriteOk(ctx, schemaJson, requestId);
        }

        private static void HandleDiagnostics(HttpListenerContext ctx)
        {
            var requestId = EnsureRequestId(ctx.Request.Headers["X-Request-Id"]);
            var json = BuildDiagnosticsJson();
            WriteOk(ctx, json, requestId);
        }

        private static string BuildDiagnosticsJson()
        {
            lock (MetricsGate)
            {
                PruneRateLimitQueue(DateTime.UtcNow);

                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"serverRunning\":").Append(IsRunning ? "true" : "false").Append(",");
                sb.Append("\"baseUrl\":").Append(JsonEscaper.Q(GetBaseUrl())).Append(",");
                sb.Append("\"tokenAuthEnabled\":").Append(GetPrefBool(TokenEnabledPrefKey, false) ? "true" : "false").Append(",");
                sb.Append("\"dangerConfirmEnabled\":").Append(GetPrefBool(DangerConfirmPrefKey, true) ? "true" : "false").Append(",");
                sb.Append("\"counters\":{");
                sb.Append("\"totalRequests\":").Append(_totalRequests).Append(",");
                sb.Append("\"totalToolCalls\":").Append(_totalToolCalls).Append(",");
                sb.Append("\"totalRejected\":").Append(_totalRejected).Append(",");
                sb.Append("\"toolCallsInCurrentWindow\":").Append(ToolCallTimestampsUtc.Count);
                sb.Append("},");
                sb.Append("\"rateLimit\":{");
                sb.Append("\"windowSeconds\":").Append(ToolCallRateLimitWindowSeconds).Append(",");
                sb.Append("\"maxRequests\":").Append(ToolCallRateLimitMaxRequests);
                sb.Append("},");
                sb.Append("\"recentCalls\":").Append(ToJsonArray(RecentCalls)).Append(",");
                sb.Append("\"recentErrors\":").Append(ToJsonArray(RecentErrors));
                sb.Append("}");
                return sb.ToString();
            }
        }

        private static bool RequiresTokenAuth(string path)
        {
            if (!GetPrefBool(TokenEnabledPrefKey, false))
                return false;
            return !string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasValidToken(HttpListenerRequest req)
        {
            var expect = GetToken();
            if (string.IsNullOrWhiteSpace(expect))
                return false;
            var provided = req.Headers["X-Unity-Bridge-Token"] ?? "";
            return string.Equals(expect, provided, StringComparison.Ordinal);
        }

        private static void EnsureTokenInitialized()
        {
            if (!GetPrefBool(TokenEnabledPrefKey, false))
                return;
            if (string.IsNullOrWhiteSpace(GetPrefString(TokenPrefKey, "")))
                SetPrefString(TokenPrefKey, GenerateToken());
        }

        private static string GetToken()
        {
            var token = GetPrefString(TokenPrefKey, "");
            if (!string.IsNullOrWhiteSpace(token))
                return token;
            token = GenerateToken();
            SetPrefString(TokenPrefKey, token);
            return token;
        }

        private static string GenerateToken()
        {
            return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        }

        private static bool TryConsumeToolCallQuota(out int retryAfterSeconds)
        {
            lock (MetricsGate)
            {
                var now = DateTime.UtcNow;
                PruneRateLimitQueue(now);
                if (ToolCallTimestampsUtc.Count >= ToolCallRateLimitMaxRequests)
                {
                    var oldest = ToolCallTimestampsUtc.Peek();
                    var wait = oldest.AddSeconds(ToolCallRateLimitWindowSeconds) - now;
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
                    return false;
                }
                ToolCallTimestampsUtc.Enqueue(now);
                retryAfterSeconds = 0;
                return true;
            }
        }

        private static void PruneRateLimitQueue(DateTime nowUtc)
        {
            var threshold = nowUtc.AddSeconds(-ToolCallRateLimitWindowSeconds);
            while (ToolCallTimestampsUtc.Count > 0 && ToolCallTimestampsUtc.Peek() < threshold)
                ToolCallTimestampsUtc.Dequeue();
        }

        private static void RecordToolCall(string tool, bool success, long elapsedMs, string requestId, string errorCode)
        {
            lock (MetricsGate)
            {
                _totalToolCalls++;
                AppendBounded(
                    RecentCalls,
                    $"{DateTime.UtcNow:O} requestId={requestId} tool={tool} success={success.ToString().ToLowerInvariant()} elapsedMs={elapsedMs} errorCode={errorCode}");
            }
        }

        private static void RecordError(string code, string message, string requestId)
        {
            lock (MetricsGate)
            {
                AppendBounded(RecentErrors, $"{DateTime.UtcNow:O} requestId={requestId} code={code} message={message}");
            }
        }

        private static void RecordRejected(string code, string message)
        {
            lock (MetricsGate)
            {
                _totalRejected++;
                AppendBounded(RecentErrors, $"{DateTime.UtcNow:O} code={code} message={message}");
            }
        }

        private static void AppendBounded(Queue<string> queue, string value)
        {
            queue.Enqueue(value);
            while (queue.Count > MaxRecentDiagnosticsItems)
                queue.Dequeue();
        }

        private static string ToJsonArray(IEnumerable<string> values)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            var first = true;
            foreach (var v in values)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append(JsonEscaper.Q(v ?? ""));
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string EnsureRequestId(string requestId)
        {
            if (!string.IsNullOrWhiteSpace(requestId))
                return requestId.Trim();
            return Guid.NewGuid().ToString("D");
        }

        private static bool GetPrefBool(string key, bool defaultValue)
        {
            if (MainThread.IsMainThread) return EditorPrefs.GetBool(key, defaultValue);
            return MainThread.Run(() => EditorPrefs.GetBool(key, defaultValue));
        }

        private static int GetPrefInt(string key, int defaultValue)
        {
            if (MainThread.IsMainThread) return EditorPrefs.GetInt(key, defaultValue);
            return MainThread.Run(() => EditorPrefs.GetInt(key, defaultValue));
        }

        private static string GetPrefString(string key, string defaultValue)
        {
            if (MainThread.IsMainThread) return EditorPrefs.GetString(key, defaultValue);
            return MainThread.Run(() => EditorPrefs.GetString(key, defaultValue));
        }

        private static void SetPrefBool(string key, bool value)
        {
            if (MainThread.IsMainThread) { EditorPrefs.SetBool(key, value); return; }
            MainThread.Run(() => EditorPrefs.SetBool(key, value));
        }

        private static void SetPrefString(string key, string value)
        {
            if (MainThread.IsMainThread) { EditorPrefs.SetString(key, value); return; }
            MainThread.Run(() => EditorPrefs.SetString(key, value));
        }
    }
}
