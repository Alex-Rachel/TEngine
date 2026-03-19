# 脚本创建与资源工作流（unity-mcp）

## 目录
- [manage_script：C# 脚本管理](#manage_scriptc-脚本管理)
- [apply_text_edits：精确文本编辑](#apply_text_edits精确文本编辑)
- [manage_asset：资源文件管理](#manage_asset资源文件管理)
- [manage_scriptable_object：ScriptableObject 读写](#manage_scriptable_objectscriptableobject-读写)
- [TEngine 脚本创建规范](#tengine-脚本创建规范)

---

## manage_script：C# 脚本管理

### 创建新脚本（带完整内容）

```json
{
  "tool": "manage_script",
  "params": {
    "action": "create",
    "name": "BattleMainUI",
    "path": "Assets/GameScripts/HotFix/GameLogic/UI/Battle",
    "contents": "using UnityEngine;\nusing GameLogic;\n\nnamespace GameLogic\n{\n    [Window(UILayer.UI, \"BattleMainUI\", fullScreen: true)]\n    public class BattleMainUI : UIWindow\n    {\n        protected override void ScriptGenerator() { }\n        protected override void RegisterEvent() { }\n        protected override void OnCreate() { }\n        protected override void OnRefresh() { }\n    }\n}",
    "namespace": "GameLogic"
  }
}
```

**脚本模板类型**（`scriptType` 参数，不传则生成默认 MonoBehaviour）：
- 不指定：空 MonoBehaviour
- 工具会自动验证语法，`validation_failed` 时需修正后重试

### 删除脚本
```json
{
  "tool": "manage_script",
  "params": {
    "action": "delete",
    "name": "OldScript",
    "path": "Assets/GameScripts/HotFix/GameLogic/UI"
  }
}
```

### 获取脚本 SHA（编辑前必须先获取）
```json
{
  "tool": "manage_script",
  "params": {
    "action": "get_sha",
    "name": "BattleMainUI",
    "path": "Assets/GameScripts/HotFix/GameLogic/UI/Battle"
  }
}
```
返回：`sha256`（后续 `apply_text_edits` 必须携带此值）

### 验证脚本语法
```json
{
  "tool": "manage_script",
  "params": {
    "action": "validate",
    "name": "BattleMainUI",
    "path": "Assets/GameScripts/HotFix/GameLogic/UI/Battle",
    "level": "standard"
  }
}
```
`level` 可选：`basic`、`standard`（默认）、`comprehensive`、`strict`（需 Roslyn）

---

## apply_text_edits：精确文本编辑

**推荐工作流：读取 → 获取 SHA → 精确修改**，避免全量覆写带来的风险。

### 完整流程
```
1. manage_script action=get_sha → 获得 sha256
2. 阅读文件内容（通过 MCP resources 或文件工具）
3. apply_text_edits 携带 sha256 + 行列范围编辑
```

### 精确替换指定行范围
```json
{
  "tool": "apply_text_edits",
  "params": {
    "name": "BattleMainUI",
    "path": "Assets/GameScripts/HotFix/GameLogic/UI/Battle",
    "precondition_sha256": "<上一步获取的sha256>",
    "edits": [
      {
        "startLine": 15,
        "startCol": 1,
        "endLine": 18,
        "endCol": 1,
        "newText": "        protected override void OnRefresh()\n        {\n            RefreshHp(PlayerData.Hp);\n        }\n"
      }
    ],
    "options": {
      "refresh": "debounced",
      "validate": "standard"
    }
  }
}
```

**重要规则**：
- `precondition_sha256` 必须与当前文件匹配，文件被修改后需重新获取
- `startCol` / `endCol` 从 1 开始计数
- 多个编辑区域**不能重叠**，按行号降序排列
- `refresh: "immediate"` 立即触发编译；`"debounced"` 延迟合并编译（推荐批量修改时）

### 错误处理
| 错误码 | 含义 | 处理 |
|--------|------|------|
| `precondition_required` | 未提供 SHA | 先调用 `get_sha` |
| `stale_file` | 文件已变更 | 重新获取 SHA |
| `unbalanced_braces` | 括号不匹配 | 检查编辑内容的括号 |
| `overlap` | 编辑区域重叠 | 按行号降序排列编辑项 |
| `using_guard` | 编辑在 using 之前 | 从 `using` 后的位置开始编辑 |

---

## manage_asset：资源文件管理

### 搜索资源
```json
{
  "tool": "manage_asset",
  "params": {
    "action": "search",
    "query": "BattleMainUI",
    "type": "Prefab",
    "path": "Assets/AssetRaw/UI/Prefabs"
  }
}
```
`type` 可选：`Prefab`、`Texture2D`、`AudioClip`、`Material`、`AnimationClip`、`ScriptableObject` 等

### 获取资源信息
```json
{
  "tool": "manage_asset",
  "params": {
    "action": "get_info",
    "path": "Assets/AssetRaw/UI/Prefabs/BattleMainUI.prefab"
  }
}
```
返回：类型、GUID、文件大小、依赖项等

### 移动/重命名资源
```json
{
  "tool": "manage_asset",
  "params": {
    "action": "move",
    "path": "Assets/OldFolder/BattleMainUI.prefab",
    "newPath": "Assets/AssetRaw/UI/Prefabs/BattleMainUI.prefab"
  }
}
```

```json
{
  "tool": "manage_asset",
  "params": {
    "action": "rename",
    "path": "Assets/AssetRaw/UI/Prefabs/BattleUI.prefab",
    "newName": "BattleMainUI"
  }
}
```

### 复制资源
```json
{
  "tool": "manage_asset",
  "params": {
    "action": "duplicate",
    "path": "Assets/AssetRaw/UI/Prefabs/BattleMainUI.prefab",
    "newPath": "Assets/AssetRaw/UI/Prefabs/BattleMainUI_Copy.prefab"
  }
}
```

### 删除资源
```json
{
  "tool": "manage_asset",
  "params": {
    "action": "delete",
    "path": "Assets/AssetRaw/UI/Prefabs/OldUI.prefab"
  }
}
```

### 创建文件夹
```json
{
  "tool": "manage_asset",
  "params": {
    "action": "create_folder",
    "path": "Assets/AssetRaw/UI/Prefabs/Battle"
  }
}
```

### 刷新 AssetDatabase
```json
{ "tool": "refresh_unity", "params": {} }
```

---

## manage_scriptable_object：ScriptableObject 读写

### 读取 ScriptableObject 字段
```json
{
  "tool": "manage_scriptable_object",
  "params": {
    "action": "read",
    "path": "Assets/GameScripts/HotFix/GameData/GameSettings.asset"
  }
}
```

### 修改 ScriptableObject 字段
```json
{
  "tool": "manage_scriptable_object",
  "params": {
    "action": "write",
    "path": "Assets/GameScripts/HotFix/GameData/GameSettings.asset",
    "properties": {
      "maxPlayers": 4,
      "defaultHealth": 100,
      "gameVersion": "1.2.0"
    }
  }
}
```

### 创建新 ScriptableObject
```json
{
  "tool": "manage_scriptable_object",
  "params": {
    "action": "create",
    "typeName": "GameSettings",
    "path": "Assets/GameScripts/HotFix/GameData/GameSettings.asset"
  }
}
```

---

## TEngine 脚本创建规范

### UIWindow 骨架模板

创建 UIWindow 时使用以下标准骨架（替换 `BattleMainUI` 为实际类名）：

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GameLogic;
using TEngine;

namespace GameLogic
{
    [Window(UILayer.UI, "BattleMainUI", fullScreen: true)]
    public class BattleMainUI : UIWindow
    {
        // --- 节点引用（UIScriptGenerator 自动生成后粘贴到这里）---
        private Button _btnBack;
        private TextMeshProUGUI _tmpTitle;

        // --- 生命周期 ---

        protected override void ScriptGenerator()
        {
            _btnBack = FindChildComponent<Button>("m_btn_Back");
            _tmpTitle = FindChildComponent<TextMeshProUGUI>("m_tmp_Title");
            RegisterButtonClick(_btnBack, OnBackClicked);
        }

        protected override void RegisterEvent()
        {
            // AddUIEvent<T>(eventId, handler);
        }

        protected override void OnCreate()
        {
            // 首次创建，只执行一次
        }

        protected override void OnRefresh()
        {
            // 每次 ShowUI 时刷新数据
        }

        protected override void OnDestroy()
        {
            // 清理 Timer 等资源（UIEvent 自动清理）
        }

        // --- 事件回调 ---

        private void OnBackClicked()
        {
            GameModule.UI.CloseUI<BattleMainUI>();
        }
    }
}
```

### UIWidget 骨架模板

```csharp
using UnityEngine;
using UnityEngine.UI;
using GameLogic;

namespace GameLogic
{
    public class SkillSlotWidget : UIWidget
    {
        private Button _btnSkill;
        private Image _imgIcon;

        protected override void ScriptGenerator()
        {
            _btnSkill = FindChildComponent<Button>("m_btn_Skill");
            _imgIcon = FindChildComponent<Image>("m_img_Icon");
            RegisterButtonClick(_btnSkill, OnSkillClicked);
        }

        public void SetData(SkillConfig config)
        {
            _imgIcon.SetSprite(config.IconPath);
        }

        private void OnSkillClicked() { }
    }
}
```

### TEngine 脚本路径约定

| 类型 | 路径 |
|------|------|
| UIWindow | `Assets/GameScripts/HotFix/GameLogic/UI/<模块名>/<ClassName>.cs` |
| UIWidget | 同上 |
| 生成代码（绑定） | `Assets/GameScripts/HotFix/GameLogic/UI/Gen/<ClassName>Gen.cs` |
| 模块代码 | `Assets/GameScripts/HotFix/GameLogic/Module/<ModuleName>/` |
| 事件接口 | `Assets/GameScripts/HotFix/GameLogic/Event/` |
| GameProto（Luban）| `Assets/GameScripts/HotFix/GameProto/`（自动生成，勿手动修改）|

### 脚本创建后的自动化流程

创建脚本后 unity-mcp 会自动触发 AssetDatabase 刷新和编译，无需手动调用 `refresh_unity`。若编译失败可通过 `read_console` 查看错误。
