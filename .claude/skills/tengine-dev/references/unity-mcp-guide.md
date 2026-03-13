# Unity MCP 操作指南（CoplayDev/unity-mcp）

## 目录
- [连接与基础信息](#连接与基础信息)
- [核心原则：batch_execute 优先](#核心原则batch_execute-优先)
- [工具全览](#工具全览)
- [目标定位方式（target）](#目标定位方式target)
- [常见错误处理](#常见错误处理)

---

## 连接与基础信息

```
MCP 服务端地址：http://localhost:8080/mcp
Unity Editor 中：Window > MCP for Unity > Start Bridge
```

**连接确认**：每次操作前无需显式 ping，直接调用工具即可。工具调用失败时再检查连接状态。

---

## 核心原则：batch_execute 优先

**批量操作比单次调用快 10~100 倍**，任何需要创建或修改多个对象的任务都应使用 `batch_execute`：

```json
{
  "tool": "batch_execute",
  "commands": [
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "Root", ... } },
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "Child", "parent": "Root", ... } },
    { "tool": "manage_components", "params": { "action": "add", "target": "Child", "componentType": "Button" } }
  ],
  "failFast": true
}
```

- 默认每批最多 25 条命令（可配置，最大 100）
- `failFast: true` 遇到第一个错误即停止（推荐）
- 命令在主线程顺序执行，保证操作原子性

---

## 工具全览

| 工具名 | 用途 | 详细文档 |
|--------|------|---------|
| `batch_execute` | 批量执行多条命令（性能关键）| 本文 |
| `manage_gameobject` | 场景中 GO 的 CRUD | [scene-gameobject.md](scene-gameobject.md) |
| `manage_scene` | 场景加载/保存/截图/层级查询 | [scene-gameobject.md](scene-gameobject.md) |
| `manage_components` | 组件添加/删除/属性设置 | [scene-gameobject.md](scene-gameobject.md) |
| `find_gameobjects` | 按名/路径/Tag/Layer 批量查找 | [scene-gameobject.md](scene-gameobject.md) |
| `manage_prefabs` | Prefab 创建/检查/修改内容 | [ui-prefab-builder.md](ui-prefab-builder.md) |
| `manage_ui` | UGUI 元素（Canvas/Button/Text等）| [ui-prefab-builder.md](ui-prefab-builder.md) |
| `manage_script` | C# 脚本 CRUD/验证/文本编辑 | [script-asset-workflow.md](script-asset-workflow.md) |
| `manage_asset` | 资源文件导入/移动/搜索/获取信息 | [script-asset-workflow.md](script-asset-workflow.md) |
| `manage_scriptable_object` | ScriptableObject 读写 | [script-asset-workflow.md](script-asset-workflow.md) |
| `manage_material` | 材质创建/属性设置/颜色 | [material-shader-vfx.md](material-shader-vfx.md) |
| `manage_shader` | Shader 文件 CRUD | [material-shader-vfx.md](material-shader-vfx.md) |
| `manage_texture` | 纹理导入设置调整 | [material-shader-vfx.md](material-shader-vfx.md) |
| `manage_vfx` | 粒子/VFX Graph/LineRenderer/Trail | [material-shader-vfx.md](material-shader-vfx.md) |
| `manage_animation` | AnimatorController/Clip/参数/状态机 | [material-shader-vfx.md](material-shader-vfx.md) |
| `manage_editor` | 进入/退出 PlayMode，Tag/Layer 管理 | [editor-automation.md](editor-automation.md) |
| `run_tests` | 启动 Test Runner 并返回 job_id | [editor-automation.md](editor-automation.md) |
| `get_test_job` | 轮询测试结果 | [editor-automation.md](editor-automation.md) |
| `read_console` | 读取 Console 日志 | [editor-automation.md](editor-automation.md) |
| `execute_menu_item` | 执行 Unity 菜单命令 | [editor-automation.md](editor-automation.md) |
| `refresh_unity` | 刷新 AssetDatabase | [editor-automation.md](editor-automation.md) |
| `execute_custom_tool` | 执行项目自定义 MCP 工具 | [editor-automation.md](editor-automation.md) |
| `apply_text_edits` | 文本精确编辑（行列范围替换）| [script-asset-workflow.md](script-asset-workflow.md) |
| `validate_script` | 验证 C# 脚本语法 | [script-asset-workflow.md](script-asset-workflow.md) |
| `get_sha` | 获取文件 SHA256（用于 precondition）| [script-asset-workflow.md](script-asset-workflow.md) |
| `find_in_file` | 在文件中搜索内容 | [script-asset-workflow.md](script-asset-workflow.md) |
| `script_apply_edits` | 另一种结构化脚本编辑 | [script-asset-workflow.md](script-asset-workflow.md) |

---

## 目标定位方式（target）

所有操作 GameObject 的工具都通过 `target` 参数定位对象，支持以下方式：

| 方式 | 示例值 | 说明 |
|------|--------|------|
| 名称（默认）| `"Canvas"` | 场景中第一个匹配的名称 |
| 层级路径 | `"UIRoot/Canvas/Panel"` | 含 `/` 时按路径查找 |
| InstanceID | `12345`（整数）| 最可靠，不受重名影响 |
| Tag | 配合 `searchMethod: "by_tag"` | 查找指定 Tag 的对象 |

> **推荐**：操作刚创建的对象时，先通过创建结果获取 `instanceID`，用 ID 做后续操作最安全。

**`searchMethod` 可选值**：`by_name`（默认）、`by_path`、`by_id`、`by_tag`

---

## 常见错误处理

| 错误信息 | 原因 | 解决 |
|---------|------|------|
| `Target not found` | 名称不存在或拼写错误 | 先 `find_gameobjects` 确认名称 |
| `Scene has unsaved changes` | 加载新场景前存在未保存改动 | 先 `manage_scene` action=save |
| `Prefab asset requires manage_prefabs` | 用 manage_gameobject 操作 .prefab 文件 | 改用 `manage_prefabs` action=modify_contents |
| `validation_failed` | 脚本语法错误 | 检查 C# 语法后重试 |
| `precondition_required` | apply_text_edits 需要 SHA | 先 `get_sha` 获取当前 SHA |
| `stale_file` | 文件已被修改 | 重新 `get_sha` 后再编辑 |
| `batch too large` | 单批命令超过限制 | 拆分为多个 batch_execute |
