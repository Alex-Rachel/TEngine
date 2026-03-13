# Editor 自动化、测试与调试（unity-mcp）

## 目录
- [manage_editor：编辑器控制](#manage_editor编辑器控制)
- [execute_menu_item：执行菜单命令](#execute_menu_item执行菜单命令)
- [run_tests & get_test_job：自动化测试](#run_tests--get_test_job自动化测试)
- [read_console：控制台日志](#read_console控制台日志)
- [refresh_unity：刷新资产库](#refresh_unity刷新资产库)
- [execute_custom_tool：执行项目自定义工具](#execute_custom_tool执行项目自定义工具)
- [完整调试工作流](#完整调试工作流)

---

## manage_editor：编辑器控制

### PlayMode 控制

```json
// 进入 Play Mode
{ "tool": "manage_editor", "params": { "action": "play" } }

// 暂停/恢复
{ "tool": "manage_editor", "params": { "action": "pause" } }

// 退出 Play Mode
{ "tool": "manage_editor", "params": { "action": "stop" } }

// 进入 Play Mode 并等待完成（等待编译完成后再进入）
{ "tool": "manage_editor", "params": { "action": "play", "waitForCompletion": true } }
```

### Tag 管理

```json
// 添加 Tag
{ "tool": "manage_editor", "params": { "action": "add_tag", "tagName": "Enemy" } }

// 删除 Tag
{ "tool": "manage_editor", "params": { "action": "remove_tag", "tagName": "Enemy" } }

// 列出所有 Tag
{ "tool": "manage_editor", "params": { "action": "list_tags" } }
```

### Layer 管理

```json
// 添加 Layer（自动选择 8~31 的空闲槽）
{ "tool": "manage_editor", "params": { "action": "add_layer", "layerName": "Enemy" } }

// 删除 Layer
{ "tool": "manage_editor", "params": { "action": "remove_layer", "layerName": "Enemy" } }

// 列出所有 Layer
{ "tool": "manage_editor", "params": { "action": "list_layers" } }
```

---

## execute_menu_item：执行菜单命令

几乎所有 Unity 编辑器菜单项都可以通过这个工具调用：

```json
{ "tool": "execute_menu_item", "params": { "menuItem": "Assets/Refresh" } }
{ "tool": "execute_menu_item", "params": { "menuItem": "File/Save Project" } }
{ "tool": "execute_menu_item", "params": { "menuItem": "Window/General/Game" } }
{ "tool": "execute_menu_item", "params": { "menuItem": "Edit/Select All" } }
```

**TEngine 常用菜单命令**：

| 操作 | menuItem 路径 |
|------|-------------|
| 刷新资源 | `Assets/Refresh` |
| 保存项目 | `File/Save Project` |
| 清理缓存 | `Tools/YooAsset/Clear Build Cache` |
| 生成 UI 脚本 | `Tools/UIScriptGenerator/Generate Selected` |
| Luban 配置表生成 | `Tools/Luban/Generate` |
| 构建 HybridCLR | `HybridCLR/Generate/All` |

---

## run_tests & get_test_job：自动化测试

### 启动测试（返回 job_id）
```json
{
  "tool": "run_tests",
  "params": {
    "mode": "EditMode",
    "filter": "GameLogic.Tests"
  }
}
```
- `mode`：`EditMode`（编辑模式测试）或 `PlayMode`（运行时测试）
- `filter`：测试类名/命名空间过滤（可选）
- 返回 `job_id`，立即返回，测试在后台运行

### 轮询测试结果
```json
{
  "tool": "get_test_job",
  "params": {
    "job_id": "<run_tests 返回的 job_id>"
  }
}
```
返回 `status`：`running`、`completed`、`failed`

**轮询策略**：每隔 2~5 秒调用一次，直到 `status` 不为 `running`。

### 清理卡住的测试任务
```json
{ "tool": "run_tests", "params": { "clear_stuck": true } }
```

---

## read_console：控制台日志

### 读取最新日志
```json
{
  "tool": "read_console",
  "params": {
    "count": 50,
    "logLevel": "All"
  }
}
```
`logLevel` 可选：`All`、`Log`、`Warning`、`Error`、`Exception`

### 只读错误日志
```json
{
  "tool": "read_console",
  "params": {
    "count": 20,
    "logLevel": "Error"
  }
}
```

### 读取特定关键词日志
```json
{
  "tool": "read_console",
  "params": {
    "count": 30,
    "filter": "BattleMainUI",
    "logLevel": "All"
  }
}
```

---

## refresh_unity：刷新资产库

脚本修改后、文件移动后使用，触发 AssetDatabase.Refresh()：

```json
{ "tool": "refresh_unity", "params": {} }
```

> 注意：`manage_script` 创建/编辑脚本后会自动刷新，通常不需要手动调用。

---

## execute_custom_tool：执行项目自定义工具

项目内 `Assets/Editor/` 目录中通过 `[McpForUnityTool]` 特性注册的自定义工具：

```json
{
  "tool": "execute_custom_tool",
  "params": {
    "toolName": "generate_ui_script",
    "params": {
      "prefabPath": "Assets/AssetRaw/UI/Prefabs/BattleMainUI.prefab"
    }
  }
}
```

---

## 完整调试工作流

### 场景 → 运行 → 读日志

```
1. 确保场景已保存：manage_scene action=save
2. 进入 Play Mode：manage_editor action=play
3. 等待 2 秒后读取日志：read_console logLevel=Error count=20
4. 如有报错，根据日志定位问题
5. 退出 Play Mode：manage_editor action=stop
6. 修复代码：apply_text_edits
7. 等待编译（约 3~5 秒后再进入 Play Mode）
```

### 编译错误排查

```
1. 修改脚本后等待编译
2. read_console logLevel=Error count=30
3. 根据错误行号使用 apply_text_edits 修复
4. 再次 read_console 确认无新错误
```

### 自动化测试工作流

```
1. run_tests mode=EditMode → 获得 job_id
2. 每 3 秒轮询 get_test_job job_id=xxx
3. status=completed 后读取测试结果
4. 若有失败，read_console logLevel=Error 查看详情
5. 修复代码后重新 run_tests
```

### TEngine 特有操作序列

**热更代码重新生成**：
```json
{
  "tool": "batch_execute",
  "commands": [
    { "tool": "execute_menu_item", "params": { "menuItem": "HybridCLR/Generate/All" } },
    { "tool": "refresh_unity", "params": {} }
  ]
}
```

**Luban 配置表重新生成**：
```json
{ "tool": "execute_menu_item", "params": { "menuItem": "Tools/Luban/Generate" } }
```

**YooAsset 资源构建**：
```json
{ "tool": "execute_menu_item", "params": { "menuItem": "Tools/YooAsset/AssetBundle Builder" } }
```
