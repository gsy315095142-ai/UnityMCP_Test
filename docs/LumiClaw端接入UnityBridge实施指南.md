# LumiClaw 端接入 Unity Bridge 实施指南

**文档日期**: 2026-04-02  
**适用范围**: LumiClaw 端（Electron + Fastify）接入 Unity 端 Bridge  
**目标**: 在不改动 LumiClaw 核心架构前提下，快速接入 Unity 工具调用能力

---

## 1. 当前前提

Unity 端已具备本地 Bridge 服务（默认 `http://127.0.0.1:6847`），并提供：

- `GET /health`
- `GET /unity/info`
- `GET /unity/tools`
- `GET /unity/openapi-lite`
- `GET /unity/diagnostics`
- `POST /unity/tools/call`

`/unity/tools/call` 已支持工具：

- `get_scene_state`
- `get_project_info`
- `execute_scene_ops`
- `generate_code`
- `create_prefab`
- `delete_assets`
- `organize_assets`

---

## 2. LumiClaw 侧接入总策略

建议按现有 LumiClaw 模式接入，不引入新通信形态：

1. 在 `src/server/routes` 新增 `unity-bridge` 路由模块；
2. 在 `src/ai/tools` 新增 Unity 工具定义；
3. 在 `chat-tool-executor-handlers.js` 增加 Unity 工具执行分支；
4. 若长任务也要调用 Unity，同步补 `src/ai/long-task-tools.js`；
5. 所有调用统一走本机 HTTP `fetch` 到 Unity Bridge。

---

## 3. 需要修改的 LumiClaw 文件（建议）

> 下列路径基于 `D:/AI_Program/LumiClaw_Win`。

- `src/server/index.js`
  - 注册新路由模块（例如 `unity-bridge.js`）。
- `src/server/routes/unity-bridge.js`（新增）
  - 提供 LumiClaw 内部 API，如 `/api/unity/*`，转发到 Unity Bridge。
- `src/ai/tools/unity.js`（新增）
  - 定义 Unity 相关 tool schema（给模型看）。
- `src/ai/tools/index.js` 或等效聚合文件
  - 把 Unity tools 合并到工具列表。
- `chat-tool-executor-handlers.js`
  - 增加 Unity tool handler，调用 `/api/unity/tools/call`。
- `src/ai/long-task-tools.js`（可选但建议）
  - 补齐长任务场景对 Unity 的同等支持。

---

## 4. LumiClaw 建议配置结构

建议在 LumiClaw 用户配置中新增：

```json
{
  "unityConnection": {
    "enabled": true,
    "baseUrl": "http://127.0.0.1:6847",
    "token": "",
    "autoReconnect": true,
    "reconnectIntervalSec": 10,
    "requestTimeoutMs": 30000
  }
}
```

说明：

- `token` 对应 Unity 端 `X-Unity-Bridge-Token`（若 Unity 启用了鉴权）。
- `requestTimeoutMs` 建议 30s，用于工具调用超时控制。

---

## 5. LumiClaw 对 Unity 的内部 API 设计（建议）

为了避免前端直接依赖 Unity 协议细节，建议先在 LumiClaw 自己的 `/api` 下封装一层：

- `GET /api/unity/health` -> 转发 Unity `/health`
- `GET /api/unity/info` -> 转发 Unity `/unity/info`
- `GET /api/unity/tools` -> 转发 Unity `/unity/tools`
- `GET /api/unity/schema` -> 转发 Unity `/unity/openapi-lite`
- `POST /api/unity/call` -> 转发 Unity `/unity/tools/call`

这样后续如果 Unity 协议细节变动，只需要改这一层。

---

## 6. `/unity/tools/call` 调用契约

请求（LumiClaw -> Unity）：

```json
{
  "tool": "execute_scene_ops",
  "arguments": {
    "operations_json": "[{\"op\":\"createPrimitive\",\"name\":\"Cube\",\"primitiveType\":\"Cube\"}]"
  },
  "requestId": "uuid-string",
  "confirm": true
}
```

请求头建议：

- `Content-Type: application/json`
- `X-Request-Id: <uuid>`
- `X-Unity-Bridge-Token: <token>`（启用鉴权时必填）

成功响应：

```json
{
  "ok": true,
  "data": {
    "tool": "execute_scene_ops",
    "canonicalTool": "execute_scene_ops",
    "result": {
      "success": true,
      "stepsCompleted": 1
    }
  },
  "error": null,
  "requestId": "uuid-string",
  "timestamp": "2026-04-02T00:00:00.000Z"
}
```

---

## 7. 错误处理映射（LumiClaw 必做）

当 Unity 返回 `ok=false` 时，按 `error.code` 映射提示：

- `UNAUTHORIZED`
  - 提示：连接凭证无效，请检查 Unity Token。
- `CONFIRM_REQUIRED`
  - 提示：该操作是高风险操作，需要确认后重试（`confirm=true`）。
- `RATE_LIMITED`
  - 提示：请求过快，请稍后重试。
- `PAYLOAD_TOO_LARGE`
  - 提示：请求体过大，请减少单次操作内容。
- `TOOL_NOT_AVAILABLE`
  - 提示：工具当前不可用（可能版本不匹配或功能关闭）。
- `EXECUTION_FAILED`
  - 提示：Unity 执行失败，展示返回 message。

---

## 8. Unity Tool 定义建议（LumiClaw 侧）

建议先只开放以下 5 个给模型（风险与稳定性平衡）：

- `get_scene_state`
- `get_project_info`
- `execute_scene_ops`
- `generate_code`
- `create_prefab`

把 `delete_assets` 和 `organize_assets` 放二期，或默认加二次确认。

---

## 9. 聊天执行器接入建议

在 `chat-tool-executor-handlers.js` 中增加统一函数（示意）：

```js
async function callUnityTool({ tool, argumentsObj, confirm = false }) {
  const requestId = crypto.randomUUID();
  const res = await fetch(`http://127.0.0.1:${serverPort}/api/unity/call`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Request-Id': requestId },
    body: JSON.stringify({
      tool,
      arguments: argumentsObj || {},
      requestId,
      confirm
    })
  });
  const json = await res.json();
  if (!json.ok) throw new Error(`[${json.error?.code}] ${json.error?.message || 'Unity call failed'}`);
  return json.data?.result;
}
```

---

## 10. 联调步骤（推荐顺序）

1. Unity 打开目标项目，确认 Bridge 在运行；  
2. LumiClaw 调 `GET /api/unity/health`（应为 up）；  
3. LumiClaw 调 `GET /api/unity/tools`，校验工具列表；  
4. 在聊天中手工触发 `get_scene_state`；  
5. 再触发 `execute_scene_ops` 创建一个 `Cube`；  
6. 校验 Unity 场景变化；  
7. 再测 `generate_code` 与 `create_prefab`；  
8. 最后测试异常路径（无 token、未 confirm、超频）。

---

## 11. 验收标准（LumiClaw 侧）

- [ ] 工具栏可显示 Unity 连接状态（绿/红）
- [ ] 至少 3 个 Unity tool 可从聊天成功调用
- [ ] `requestId` 在 LumiClaw 日志和 Unity 响应中可追踪
- [ ] `UNAUTHORIZED` / `CONFIRM_REQUIRED` / `RATE_LIMITED` 有明确用户提示
- [ ] Unity 断开时，聊天侧能给出可操作的引导

---

## 12. 后续建议

- 接入 `GET /unity/diagnostics` 到 LumiClaw 调试面板；
- 在 LumiClaw 侧加入“危险操作确认 UI”；
- 二期再接入标准 MCP 模式，Bridge 作为兼容 fallback。

---

**备注**: 若后续需要，我可以再补一版“按 LumiClaw 实际文件逐段改造的 patch 计划”，可直接照着提交代码。
