# LumiClaw × Unity MCP Tools — 工具栏图标设计文档

**文档创建日期**: 2026-03-22
**关联项目**: UnityMCP（Unity AI 辅助开发插件）
**适用产品**: LumiClaw AI 助手桌面端
**状态**: 设计稿（待实现）

---

## 1. 背景与目标

### 1.1 背景（现状校正）

LumiClaw 是一款 AI 助手桌面客户端；本设计目标是让它能够连接 Unity 侧能力并在对话中调用相关 Tools。  
需要说明的是：**当前 UnityMCP 代码库尚未提供完整的标准 MCP Server（SSE/tools/list/serverInfo）对外服务**，现有实现主要是 Unity 编辑器内的 AI 工具调用循环（function-calling）。

因此，本方案采用分阶段路径：

- **Phase A（可落地）**：先通过本地 Bridge（HTTP/IPC）打通 LumiClaw -> Unity 控制链路；
- **Phase B（标准化）**：再补齐标准 MCP Server 能力，切换到 `mcpServers` 统一管理。

### 1.2 设计目标

| 目标 | 说明 |
|------|------|
| **零门槛配置** | 新用户看到图标就能直觉性地完成配置 |
| **状态一目了然** | 已配置用户一眼知道 Unity 是否在线 |
| **不浪费界面空间** | 一个图标同时承担「配置入口」和「状态指示」两个职责 |
| **可复用模式** | 图标设计模式可扩展到其他 MCP Server（Blender、Figma 等） |

---

## 2. 整体设计思路

**核心概念：一个 Unity 图标，两种状态模式。**

```
┌──────────────────────────────────────────────────┐
│  LumiClaw 工具栏                                  │
│                                                  │
│  [...现有图标...]  [Unity图标]  [...现有图标...]   │
│                                                  │
└──────────────────────────────────────────────────┘
```

- **未配置**：图标为灰色/未激活态 → 点击弹出配置面板
- **已配置**：图标为彩色状态态 → 🟢绿色表示已连接，🔴红色表示未连接 → 点击弹出状态面板

---

## 3. 状态机与图标表现

### 3.1 三种状态

```
                    配置完成并保存
  ┌──────────┐  ──────────────────>  ┌──────────────┐
  │ 未配置    │                       │  已配置        │
  │ (⚪灰底)  │  <──────────────────  │              │
  └──────────┘   点击"清除配置"       │  ┌─🟢已连接   │
                                        │  │            │
                                        │  └─🔴未连接   │
                                        │              │
                                        └──────────────┘
```

### 3.2 图标样式详细说明

| 状态 | 图标底色 | 角标/装饰 | Tooltip 文案 |
|------|---------|----------|-------------|
| **未配置** | 灰色（#999 / opacity 0.5） | 右上角小 `⚙` 齿轮角标 | `Unity MCP — 点击配置` |
| **已配置 + 已连接** | 正常色彩（Unity Logo 原色） | 右下角绿色小圆点 `●` | `Unity MCP 已连接 · N 个工具可用` |
| **已配置 + 未连接** | 正常色彩但略暗（opacity 0.7） | 右下角红色小圆点 `●` | `Unity MCP 未连接` |

**图标选择建议**：
- 使用 Unity 官方 Logo 图标（黑白简洁版，便于叠加状态色）
- 或自定义简化版：一个立方体 + "U" 字的组合图标

---

## 4. 交互流程

### 4.1 未配置 → 点击 → 配置面板

```
用户点击灰色 Unity 图标
        │
        ▼
┌─────────────────────────────────────┐
│  🔧 配置 Unity MCP 连接             │
├─────────────────────────────────────┤
│                                     │
│  连接地址：                           │
│  ┌─────────────────────────────┐   │
│  │ http://localhost:6847        │   │
│  └─────────────────────────────┘   │
│                                     │
│  连接模式：                          │
│  ○ Unity Bridge（当前） ○ 标准 MCP  │
│                                     │
│  📎 提示：                          │
│  请确保 Unity 编辑器已打开，          │
│  且已安装 UnityMCP 插件。            │
│                                     │
│           [取消]    [保存配置]       │
└─────────────────────────────────────┘
```

**配置面板字段说明**：

| 字段 | 说明 | 默认值 | 必填 |
|------|------|--------|------|
| 连接地址 | Unity Bridge 或 MCP Server 地址 | `http://localhost:6847` | ✅ |
| 连接模式 | `bridge` 或 `mcp` | `bridge` | ✅ |
| 自动重连 | 断开后是否自动尝试重连 | 开启（默认） | 可选 |
| 重连间隔 | 自动重连的间隔时间 | 10 秒 | 可选 |

> 注：`bridge` 为当前推荐模式（与现有代码现状一致）；`mcp` 为后续标准化模式。

**保存后的行为**：
1. 立即按当前模式（Bridge 或 MCP）发起连接
2. 连接成功 → 图标变为 🟢绿色，弹出轻提示「✅ Unity MCP 连接成功」
3. 连接失败 → 图标变为 🔴红色，弹出提示「❌ 无法连接，请确认 Unity 编辑器已打开」

### 4.2 已配置 + 已连接 🟢 → 点击 → 状态面板

```
用户点击绿色 Unity 图标
        │
        ▼
┌─────────────────────────────────────┐
│  🟢 Unity MCP Tools                 │
├─────────────────────────────────────┤
│                                     │
│  状态：     已连接                    │
│  地址：     localhost:6847           │
│  协议：     SSE                      │
│  编辑器版本：Unity 2022.3.13f1c1    │
│  可用工具：12 个                     │
│                                     │
│  ── 可用工具列表 ──────────────────  │
│  • get_scene_state     场景状态快照  │
│  • execute_scene_ops   批量场景操作  │
│  • generate_code       生成脚本      │
│  • create_prefab       创建预制体    │
│  • delete_assets       删除资源      │
│  • organize_assets     整理资源      │
│  • get_project_info    工程信息      │
│  • ...                              │
│                                     │
│     [重新配置]    [断开连接]         │
└─────────────────────────────────────┘
```

**状态面板交互**：

| 按钮 | 行为 |
|------|------|
| **重新配置** | 打开配置面板（同未配置时的配置面板），预填当前配置值 |
| **断开连接** | 主动断开 MCP 连接；图标变为 🔴；下次可自动重连或手动点击图标重连 |

### 4.3 已配置 + 未连接 🔴 → 点击 → 状态面板

```
用户点击红色 Unity 图标
        │
        ▼
┌─────────────────────────────────────┐
│  🔴 Unity MCP Tools                 │
├─────────────────────────────────────┤
│                                     │
│  状态：     未连接                    │
│  地址：     localhost:6847           │
│                                     │
│  ⚠️ 可能原因：                      │
│  • Unity 编辑器未打开               │
│  • UnityMCP 插件未安装               │
│  • MCP Server 端口被占用             │
│                                     │
│           [重新连接]  [重新配置]      │
└─────────────────────────────────────┘
```

**未连接时的补充行为**：

| 场景 | LumiClaw 的处理 |
|------|----------------|
| 用户说「创建一个 Cube」但 Unity 未连接 | 弹出提示：*"Unity MCP 未连接，请确认 Unity 编辑器已打开，或点击工具栏 Unity 图标检查配置。"* |
| 自动重连开启时 | 后台每 N 秒尝试一次连接，连接成功后图标自动变 🟢，可选弹出轻提示 |
| 自动重连关闭时 | 保持 🔴 状态，用户需手动点击「重新连接」 |

---

## 5. 技术设计（校正版）

### 5.1 配置存储

建议将 Unity 连接配置存储在 LumiClaw 用户配置中，并支持双模式（Bridge / MCP）：

```jsonc
// lumiclaw 用户配置（示例）
{
  "unityConnection": {
    "mode": "bridge", // bridge | mcp
    "bridge": {
      "url": "http://localhost:6847",
      "autoReconnect": true,
      "reconnectInterval": 10
    },
    "mcp": {
      "url": "http://localhost:6847/sse",
      "type": "sse",
      "autoReconnect": true,
      "reconnectInterval": 10
    }
  }
}
```

**关键点**：
- 配置由「工具栏图标配置面板」写入，优先写入 `unityConnection.mode=bridge`
- `mcpServers` 作为后续标准化能力保留，不作为第一阶段唯一依赖
- 配置保存后，由统一连接管理器按 `mode` 选择 Bridge 客户端或 MCP 客户端

### 5.2 状态检测机制

```
                    LumiClaw Unity 连接管理器
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
         连接成功     连接中...      连接失败
              │            │            │
              ▼            ▼            ▼
     通知工具栏图标    旋转/脉冲     通知工具栏图标
       更新为 🟢       动画(可选)     更新为 🔴
```

| 检测方式 | 说明 |
|---------|------|
| **Bridge 健康检查（当前）** | 定期请求 Bridge 的健康接口（如 `/health`），超时则判定断开 |
| **MCP 连接事件（后续）** | MCP Client 的 `onConnect` / `onDisconnect` / `onError` 回调 |
| **工具列表刷新** | Bridge 模式用本地映射表；MCP 模式可通过 `tools/list` 动态获取 |

### 5.3 工具注册与可用性

当 Unity 连接成功后，Unity 相关 Tools 加入 LumiClaw 可用 Tool 列表：

```
Unity Bridge / MCP Server 启动
        │
        ▼ (LumiClaw 连接)
LumiClaw 获取 Tools（Bridge 映射或 MCP 动态列表）
        │
        ▼
┌────────────────────────────────────────┐
│  LumiClaw 已注册 Tools（合并视图）      │
│                                        │
│  🔧 通用工具                           │
│  ├─ web_search                        │
│  ├─ generate_image                    │
│  └─ ...                               │
│                                        │
│  🎮 Unity Tools（来自 unity-bridge / mcp）│
│  ├─ get_scene_state                   │
│  ├─ execute_scene_ops                 │
│  ├─ generate_code                     │
│  ├─ create_prefab                     │
│  ├─ delete_assets                     │
│  ├─ organize_assets                   │
│  └─ ...                               │
└────────────────────────────────────────┘
```

**断开时**：Unity 相关 Tools 自动标记不可用，AI 调用时提示用户检查连接状态。

### 5.4 状态面板中的编辑器信息获取

状态面板展示的「Unity 版本」「可用工具数」等信息，按连接模式分别获取：

| 信息 | Bridge 模式（当前） | MCP 模式（后续） |
|------|--------------------|-----------------|
| Unity 版本 | Bridge 状态接口返回 | `serverInfo` |
| 可用工具列表 | 本地映射表 + Bridge 能力探测 | `tools/list` |
| 活动场景名 | 通过 `get_scene_state` 解析 | 查询类 Tool 或扩展 metadata |

---

## 6. 扩展性设计（多 MCP Server 图标）

本设计模式不限于 Unity，可自然扩展到其他 MCP Server：

```
LumiClaw 工具栏

 [...现有图标...]  [🎮 Unity]  [🎨 Blender]  [📐 Figma]  [...]
                    🟢已连接     ⚪未配置       🔴未连接
```

### 6.1 图标注册机制

建议为每个 MCP Server 提供图标配置：

```jsonc
{
  "mcpServers": {
    "unity-editor": {
      "url": "http://localhost:6847/sse",
      "type": "sse",
      // 图标配置
      "icon": "unity",          // 预置图标名 或 自定义 SVG 路径
      "label": "Unity MCP",     // 显示名称
      "showInToolbar": true      // 是否在工具栏显示图标
    },
    "blender-mcp": {
      "url": "http://localhost:6848/sse",
      "type": "sse",
      "icon": "blender",
      "label": "Blender MCP",
      "showInToolbar": true
    }
  }
}
```

### 6.2 预置图标库

LumiClaw 可内置常用软件的图标：

| 图标 ID | 对应软件 |
|---------|---------|
| `unity` | Unity Editor |
| `blender` | Blender |
| `figma` | Figma |
| `vscode` | VS Code |
| `github` | GitHub |
| `custom` | 用户自定义 SVG |

---

## 7. 边界情况与错误处理

| 场景 | 处理方式 |
|------|---------|
| 配置了错误的地址 | 保存时校验 URL 格式；连接失败时提示"请检查地址是否正确" |
| Unity 编辑器中途关闭 | 心跳检测发现断开 → 图标变 🔴 → 弹出轻提示"Unity 连接已断开" |
| 多个 Unity 实例运行 | MCP Server 应仅启动一个；若端口冲突，提示用户 |
| 用户在对话中调用 Unity Tool 但已断开 | AI 层拦截 → 弹出提示引导用户检查连接状态 |
| 网络代理/防火墙拦截 | 连接超时后提示"可能被防火墙拦截，请检查网络设置" |
| LumiClaw 启动时 Unity 已经在运行 | 启动时自动尝试连接所有已配置的 MCP Server |

---

## 8. 用户体验细节

### 8.1 首次引导

当用户首次使用 LumiClaw 且检测到未配置任何 MCP Server 时：
- 工具栏可以显示一个 `+` 按钮或引导提示：「连接外部工具以增强 AI 能力 →」
- 点击后展示可连接的 MCP Server 类型列表（Unity、Blender 等）

### 8.2 连接动画

| 时机 | 动画效果 |
|------|---------|
| 正在连接中 | 图标轻微旋转或脉冲闪烁 |
| 连接成功 | 图标短暂放大弹跳 → 回到正常大小 |
| 连接断开 | 图标短暂抖动 → 变为红色 |

### 8.3 通知策略

| 事件 | 通知方式 |
|------|---------|
| 首次连接成功 | Toast 提示「✅ Unity MCP 已连接」 |
| 自动重连成功 | 静默（不打扰），仅图标变色 |
| 连接意外断开 | Toast 提示「⚠️ Unity 连接已断开」 |
| 配置保存成功 | Toast 提示「✅ 配置已保存」 |

---

## 9. 与 UnityMCP 插件的对接说明（校正版）

### 9.1 Unity 端需提供的能力（目标能力，待实现）

| 能力 | 说明 | 优先级 |
|------|------|--------|
| Bridge 服务启动 | Unity 编辑器打开后启动本地 Bridge（HTTP/IPC） | P0 |
| Bridge Tool 路由 | 能接收并执行 LumiClaw 转发的 Unity 操作请求 | P0 |
| MCP Server 启动 | 补齐标准 MCP SSE/streamable 能力 | P1 |
| Server Info / 心跳 | 提供版本、项目名、健康状态等信息 | P1 |

### 9.2 Tools 清单（与当前 UnityMCP 代码对齐）

| Tool 名称 | 功能 | 输入参数（摘要） |
|-----------|------|------------------|
| `get_scene_state` | 获取场景状态/层级快照 | 可选过滤参数 |
| `get_project_info` | 获取工程信息摘要 | (无) |
| `execute_scene_ops` | 执行批量场景操作 | `operations_json` |
| `generate_code` | 生成/保存脚本代码 | `spec_json` |
| `create_prefab` | 创建预制体资源 | `prefab_json` |
| `delete_assets` | 删除 Assets 下资源 | `asset_paths_json` |
| `organize_assets` | 移动/复制/重命名/建目录 | `operations_json` |
| `reply` | 返回最终自然语言结果 | `content` |

> 说明：文档中的旧命名（如 `create-gameobject`、`set-parent`）可作为 UI 展示别名；底层协议应优先使用上述实际工具名。

### 9.3 换项目时的行为

**Unity Tools 为编辑器级能力，但受当前项目资源内容影响。** 换项目时：

1. 在新 Unity 项目中导入 UnityMCP 插件（推荐打包为 UPM 包或 `.unitypackage`）
2. 打开 Unity 编辑器 → Bridge（P0）或 MCP Server（P1）启动
3. LumiClaw 自动检测到连接 → 图标变 🟢
4. **无需重新配置 LumiClaw**

唯一需要注意的是：新项目中的预制体名、脚本类名、资源路径会变化，用户在对话中需使用新项目真实名称/路径。

---

## 10. 实施建议

### 10.1 分阶段实施（更新）

| 阶段 | 内容 | 说明 |
|------|------|------|
| **P0 — Bridge 打通** | 单个 Unity 图标 + 配置面板 + Bridge 连接状态（🟢🔴） | 最快形成可用链路 |
| **P1 — 体验完善** | 状态面板详细信息 + 自动重连 + 断开提示 | 提升日常使用体验 |
| **P2 — MCP 标准化** | 引入/切换标准 MCP 连接与 `mcpServers` 管理 | 与多工具生态统一 |
| **P3 — 多 Server 扩展** | 多 MCP Server 图标支持 + 图标配置 + 预置图标库 | 为 Blender/Figma 等铺路 |

### 10.2 技术要点备忘

- 图标组件建议封装为通用的 `McpServerToolbarIcon` 组件，接收 `serverId`、`config`、`status` 等属性
- 配置面板和状态面板可复用同一个弹出容器，根据状态切换内容
- 连接状态管理应集中化（LumiClaw 侧的 MCP 连接管理器），避免各组件各自维护状态
- 心跳检测间隔建议 5-10 秒，避免过于频繁影响性能

---

## 11. 接口契约（Bridge 模式，P0/P1 必选）

本节定义 LumiClaw 与 Unity 之间在 **Bridge 模式** 的最小可用 API 契约，用于前后端并行开发与联调。

### 11.1 基础约定

| 项 | 约定 |
|----|------|
| Base URL | `http://127.0.0.1:6847`（默认，可配置） |
| Content-Type | `application/json; charset=utf-8` |
| 鉴权 | P0 可无鉴权；P1 建议 `X-Unity-Bridge-Token` |
| Trace ID | 请求头 `X-Request-Id`（LumiClaw 生成 UUID） |
| 时间格式 | `ISO-8601`（UTC） |

### 11.2 通用响应结构

```json
{
  "ok": true,
  "data": {},
  "error": null,
  "requestId": "2aa6a6b6-9c45-4f32-8ee9-0f0f3a8d730d",
  "timestamp": "2026-04-02T08:00:00.000Z"
}
```

失败时：

```json
{
  "ok": false,
  "data": null,
  "error": {
    "code": "UNITY_NOT_RUNNING",
    "message": "Unity Editor is not available",
    "details": {
      "hint": "请先打开 Unity 编辑器并加载项目"
    }
  },
  "requestId": "2aa6a6b6-9c45-4f32-8ee9-0f0f3a8d730d",
  "timestamp": "2026-04-02T08:00:00.000Z"
}
```

### 11.3 必需接口清单（MVP）

| 接口 | 方法 | 说明 | 关联 Tool |
|------|------|------|----------|
| `/health` | GET | Bridge 健康检查与 Unity 在线状态 | 图标状态检测 |
| `/unity/info` | GET | Unity 版本、项目名、模式、可用能力 | 状态面板 |
| `/unity/tools` | GET | 可用 Tool 列表（Bridge 映射） | 状态面板/调试 |
| `/unity/tools/call` | POST | 执行指定 Tool | 对话工具调用 |

### 11.4 接口示例

`GET /health` 返回：

```json
{
  "ok": true,
  "data": {
    "bridgeStatus": "up",
    "unityStatus": "connected",
    "mode": "bridge"
  },
  "error": null
}
```

`POST /unity/tools/call` 请求：

```json
{
  "tool": "execute_scene_ops",
  "arguments": {
    "operations_json": "[{\"op\":\"create\",\"name\":\"Cube\"}]"
  },
  "requestId": "5d415016-c734-4f79-8b6d-100a5d8f2451"
}
```

`POST /unity/tools/call` 成功响应：

```json
{
  "ok": true,
  "data": {
    "tool": "execute_scene_ops",
    "result": {
      "success": true,
      "summary": "已创建 1 个对象"
    }
  },
  "error": null,
  "requestId": "5d415016-c734-4f79-8b6d-100a5d8f2451"
}
```

### 11.5 Tool 参数契约（Bridge）

| Tool | arguments（JSON） | 备注 |
|------|-------------------|------|
| `get_scene_state` | `{}` 或过滤参数 | 返回层级、活动场景等 |
| `get_project_info` | `{}` | 返回脚本/资源摘要 |
| `execute_scene_ops` | `{ "operations_json": "..." }` | `operations_json` 为 JSON 字符串 |
| `generate_code` | `{ "spec_json": "..." }` | `spec_json` 为 JSON 字符串 |
| `create_prefab` | `{ "prefab_json": "..." }` | `prefab_json` 为 JSON 字符串 |
| `delete_assets` | `{ "asset_paths_json": "..." }` | `asset_paths_json` 为 JSON 数组字符串 |
| `organize_assets` | `{ "operations_json": "..." }` | `asset-ops` JSON 数组字符串 |
| `reply` | `{ "content": "..." }` | 用于结束性回复 |

> 兼容规则：Bridge 层允许接收 alias（如 `create-gameobject`），但必须在入口统一转换为当前标准 tool 名。

### 11.6 Bridge 与 MCP 能力映射

| 语义能力 | Bridge（P0/P1） | MCP（P2） |
|----------|------------------|-----------|
| 服务探活 | `GET /health` | 客户端连接事件 + ping |
| 获取工具列表 | `GET /unity/tools` | `tools/list` |
| 调用工具 | `POST /unity/tools/call` | `tools/call` |
| 服务元信息 | `GET /unity/info` | `serverInfo` |
| 配置模型 | `unityConnection.mode=bridge` | `unityConnection.mode=mcp` + `mcpServers` |

---

## 12. 稳定性与安全策略

### 12.1 超时与重试策略

| 场景 | 超时 | 重试策略 | 说明 |
|------|------|---------|------|
| `GET /health` | 2s | 最多 2 次，指数退避（1s/2s） | 用于状态灯刷新 |
| `GET /unity/info` | 3s | 最多 1 次 | 面板打开时拉取 |
| `GET /unity/tools` | 5s | 最多 1 次 | 工具数量展示 |
| `POST /unity/tools/call` | 30s（默认） | 可选 1 次（仅幂等工具） | `execute_scene_ops` 默认不自动重试 |

### 12.2 幂等与去重

| Tool | 幂等性 | 建议 |
|------|--------|------|
| `get_scene_state` / `get_project_info` | 是 | 可安全重试 |
| `execute_scene_ops` | 否（通常） | 禁止自动重试；需用户确认 |
| `generate_code` / `create_prefab` | 视实现而定 | 建议使用唯一输出路径避免覆盖 |
| `delete_assets` | 否 | 必须二次确认或回收站策略 |
| `organize_assets` | 否 | 建议支持预览与撤销计划 |

### 12.3 错误码规范

| code | HTTP | 语义 | 前端提示建议 |
|------|------|------|-------------|
| `BAD_REQUEST` | 400 | 参数格式错误/缺失 | 请检查参数格式 |
| `UNAUTHORIZED` | 401 | Token 无效或缺失 | 连接凭证无效，请重新配置 |
| `UNITY_NOT_RUNNING` | 503 | Unity 未运行或未就绪 | 请先打开 Unity 编辑器 |
| `TOOL_NOT_AVAILABLE` | 409 | Tool 不可用/未注册 | 当前工具不可用，请检查连接 |
| `EXECUTION_FAILED` | 500 | Tool 执行失败 | 操作失败，请查看详情 |
| `TIMEOUT` | 504 | 调用超时 | Unity 响应超时，请重试 |
| `RATE_LIMITED` | 429 | 请求过于频繁 | 请求过快，请稍后再试 |

### 12.4 安全基线（P1 开始执行）

- Bridge 仅监听 `127.0.0.1`，禁止默认对局域网暴露。
- 引入可轮换 token（最少 128-bit 随机值）。
- `delete_assets`、批量 `organize_assets`、高风险 `execute_scene_ops` 需用户确认。
- 日志默认脱敏：不记录用户完整 prompt、仅记录摘要和 requestId。

---

## 13. 验收标准与测试用例

### 13.1 功能验收（DoD）

| 编号 | 验收项 | 通过标准 |
|------|--------|---------|
| F-01 | 未配置状态展示 | 启动后显示灰色图标，点击可打开配置面板 |
| F-02 | 配置保存与即时连接 | 保存后 3 秒内出现连接结果（🟢/🔴） |
| F-03 | 状态面板信息完整 | 能显示地址、模式、Unity 版本、工具数 |
| F-04 | 工具调用可用 | 至少 `get_scene_state` 与 `execute_scene_ops` 调用成功 |
| F-05 | 断线恢复 | Unity 重启后自动恢复连接（自动重连开启时） |

### 13.2 异常验收

| 编号 | 场景 | 预期 |
|------|------|------|
| E-01 | 地址配置错误 | 显示 `BAD_REQUEST`/连接失败提示，不崩溃 |
| E-02 | Unity 关闭中调用工具 | 返回 `UNITY_NOT_RUNNING`，并引导用户重连 |
| E-03 | Tool 参数非法 | 返回 `BAD_REQUEST`，状态不被污染 |
| E-04 | 高风险操作失败 | 返回 `EXECUTION_FAILED`，UI 保持可恢复状态 |
| E-05 | 连续超时 | 进入降级（红点+重连按钮），不阻塞主界面 |

### 13.3 联调检查清单

- LumiClaw 端可记录每次请求的 `requestId`，并在错误弹窗中展示。
- Unity 端日志可按 `requestId` 检索到执行链路。
- 同一 `requestId` 在 LumiClaw 与 Unity 日志中可一一对应。
- Bridge 模式与 MCP 模式切换后，图标状态机行为一致。

---

## 附录 A：交互流程图（完整版）

```
                    ┌─────────────────────┐
                    │  LumiClaw 启动      │
                    └──────────┬──────────┘
                               ▼
                    ┌─────────────────────┐
                    │  读取 unityConnection│
                    │  与 mcpServers 配置  │
                    └──────────┬──────────┘
                               ▼
                    ┌─────────────────────┐
                    │  有 unity-editor    │──── 否 ──→ 显示 ⚪未配置图标
                    │  配置？             │
                    └──────────┬──────────┘
                              是
                               ▼
                    ┌─────────────────────┐
                    │  按 mode 尝试连接    │
                    │  bridge / mcp       │
                    └──────────┬──────────┘
                               ▼
                   ┌───────────┴───────────┐
                   ▼                       ▼
            连接成功 🟢               连接失败 🔴
                   │                       │
                   ▼                       ▼
          注册 Unity Tools         后台自动重连
          图标显示 🟢               (如已开启)
                   │                       │
                   ▼                       ▼
          用户可正常使用           提示可能原因
          Unity 相关指令           等待手动/自动重连
```

---

## 附录 B：术语表（更新）

| 术语 | 说明 |
|------|------|
| **MCP** | Model Context Protocol，AI 模型与外部工具通信的标准协议 |
| **MCP Server** | 运行在工具端的 MCP 服务，暴露 Tools 供 AI 调用 |
| **MCP Client** | 运行在 AI 宿主端的 MCP 客户端，负责连接 Server 并转发 Tool 调用 |
| **SSE** | Server-Sent Events，一种 HTTP 长连接协议，MCP 支持的传输方式之一 |
| **Tool** | MCP 中定义的可调用操作，有名称、描述、参数 Schema |
| **UnityMCP** | 本项目开发的 Unity 编辑器插件；当前以编辑器内工具调用为主，标准 MCP Server 为后续目标能力 |

---

**文档结束** — 后续实现时可根据实际情况调整细节。
