# TEngine UI Prefab 拼接规范（unity-mcp）

## 目录
- [TEngine 节点命名前缀与 unity-mcp 工具对照](#tengine-节点命名前缀与-unity-mcp-工具对照)
- [Prefab 强制结构要求](#prefab-强制结构要求)
- [标准工作流：从零拼一个 UIWindow Prefab](#标准工作流从零拼一个-uiwindow-prefab)
- [manage_ui 工具速查](#manage_ui-工具速查)
- [常用 Prefab 骨架模板](#常用-prefab-骨架模板)
- [Prefab headless 编辑（修改已有 Prefab）](#prefab-headless-编辑修改已有-prefab)

---

## TEngine 节点命名前缀与 unity-mcp 工具对照

TEngine 的 `UIScriptGenerator` 通过节点名前缀自动绑定组件。**节点名必须严格按前缀命名**，否则脚本生成器无法识别。

| TEngine 前缀 | 绑定类型 | unity-mcp 创建方式 |
|-------------|---------|------------------|
| `m_btn_xxx` | `Button`（自动注册点击）| `manage_ui` action=`create_button` |
| `m_tmp_xxx` | `TextMeshProUGUI` | `manage_ui` action=`create_text`（自动检测 TMP）|
| `m_text_xxx` | `Text` | `manage_ui` action=`create_text` |
| `m_img_xxx` | `Image` | `manage_ui` action=`create_image` |
| `m_rimg_xxx` | `RawImage` | `manage_gameobject` + `manage_components` add `RawImage` |
| `m_scroll_xxx` | `ScrollRect` | `manage_gameobject` + `manage_components` add `ScrollRect` |
| `m_scrollBar_xxx` | `Scrollbar` | `manage_gameobject` + `manage_components` add `Scrollbar` |
| `m_slider_xxx` | `Slider` | `manage_ui` action=`create_slider` |
| `m_toggle_xxx` | `Toggle` | `manage_ui` action=`create_toggle` |
| `m_group_xxx` | `ToggleGroup` | `manage_gameobject` + `manage_components` add `ToggleGroup` |
| `m_input_xxx` | `InputField` | `manage_ui` action=`create_inputfield` |
| `m_tInput_xxx` | `TMP_InputField` | `manage_gameobject` + `manage_components` add `TMP_InputField` |
| `m_go_xxx` | `GameObject` | `manage_gameobject` action=`create` |
| `m_tf_xxx` | `Transform` | `manage_gameobject` action=`create` |
| `m_rect_xxx` | `RectTransform` | `manage_gameobject` action=`create` |
| `m_grid_xxx` | `GridLayoutGroup` | `manage_gameobject` + `manage_components` add `GridLayoutGroup` |
| `m_hlay_xxx` | `HorizontalLayoutGroup` | `manage_gameobject` + `manage_components` add `HorizontalLayoutGroup` |
| `m_vlay_xxx` | `VerticalLayoutGroup` | `manage_gameobject` + `manage_components` add `VerticalLayoutGroup` |
| `m_canvasGroup_xxx` | `CanvasGroup` | `manage_gameobject` + `manage_components` add `CanvasGroup` |
| `m_item_xxx` | `UIWidget` 子类 | `manage_gameobject` + 挂对应 Widget 脚本 |

---

## Prefab 强制结构要求

**UIModule 在加载 Prefab 时强制检查根节点必须有 Canvas 组件**，否则窗口无法显示。

```
BattleMainUI.prefab（根节点）
├── [Canvas 组件] ← 必须！
├── [CanvasScaler 组件] ← 强烈建议
├── [GraphicRaycaster 组件] ← 交互必须
└── 子节点（UI 元素）
    ├── m_btn_Back
    ├── m_tmp_Title
    ├── m_tf_Content
    │   ├── m_text_Desc
    │   └── m_img_Icon
    └── m_scroll_List
        └── m_tf_ListContent
```

**Prefab 存放路径**：`Assets/AssetRaw/UI/Prefabs/<PrefabName>.prefab`

---

## 标准工作流：从零拼一个 UIWindow Prefab

### 步骤概览

```
1. 在场景中创建临时 GameObject（根节点）
2. batch_execute 批量创建所有子节点
3. 将根节点保存为 Prefab 资源
4. 清理场景（删除临时 GO）
```

### Step 1：创建根节点（含 Canvas 组件）

```json
{
  "tool": "manage_gameobject",
  "params": {
    "action": "create",
    "name": "BattleMainUI",
    "componentType": "Canvas"
  }
}
```

追加必须组件：
```json
{
  "tool": "batch_execute",
  "commands": [
    {
      "tool": "manage_components",
      "params": {
        "action": "add",
        "target": "BattleMainUI",
        "componentType": "CanvasScaler"
      }
    },
    {
      "tool": "manage_components",
      "params": {
        "action": "add",
        "target": "BattleMainUI",
        "componentType": "GraphicRaycaster"
      }
    }
  ]
}
```

### Step 2：batch_execute 批量创建所有 UI 子节点

```json
{
  "tool": "batch_execute",
  "commands": [
    {
      "tool": "manage_ui",
      "params": {
        "action": "create_button",
        "name": "m_btn_Back",
        "parent": "BattleMainUI",
        "text": "返回",
        "x": -400, "y": 250,
        "width": 120, "height": 60
      }
    },
    {
      "tool": "manage_ui",
      "params": {
        "action": "create_text",
        "name": "m_tmp_Title",
        "parent": "BattleMainUI",
        "text": "战斗界面",
        "fontSize": 36,
        "x": 0, "y": 250
      }
    },
    {
      "tool": "manage_ui",
      "params": {
        "action": "create_image",
        "name": "m_img_Background",
        "parent": "BattleMainUI",
        "width": 1920, "height": 1080,
        "r": 0, "g": 0, "b": 0, "a": 0.5
      }
    },
    {
      "tool": "manage_gameobject",
      "params": {
        "action": "create",
        "name": "m_tf_SkillPanel",
        "parent": "BattleMainUI"
      }
    },
    {
      "tool": "manage_ui",
      "params": {
        "action": "create_text",
        "name": "m_text_Hp",
        "parent": "BattleMainUI",
        "text": "HP: 100",
        "fontSize": 24,
        "x": -400, "y": -200
      }
    }
  ],
  "failFast": true
}
```

### Step 3：保存为 Prefab（headless 方式，无 UI）

```json
{
  "tool": "manage_prefabs",
  "params": {
    "action": "create_from_gameobject",
    "target": "BattleMainUI",
    "prefabPath": "Assets/AssetRaw/UI/Prefabs/BattleMainUI.prefab",
    "allowOverwrite": false
  }
}
```

### Step 4：清理场景中的临时 GameObject

```json
{
  "tool": "manage_gameobject",
  "params": {
    "action": "delete",
    "target": "BattleMainUI"
  }
}
```

---

## manage_ui 工具速查

所有 `manage_ui` 操作的通用参数：
- `name`：节点名（按 TEngine 前缀规范，如 `m_btn_Back`）
- `parent`：父节点名称或路径
- `x`, `y`：相对父节点的位置偏移
- `width`, `height`：尺寸

| action | 特有参数 |
|--------|---------|
| `create_canvas` | `renderMode`（ScreenSpaceOverlay/ScreenSpaceCamera/WorldSpace）|
| `create_panel` | `r/g/b/a`（背景色，默认半透明白）|
| `create_button` | `text`（按钮文字）|
| `create_text` | `text`, `fontSize`, `r/g/b/a`（文字颜色）|
| `create_image` | `spritePath`（sprite 资源路径，可空）, `r/g/b/a` |
| `create_inputfield` | `placeholder`（占位文字）|
| `create_slider` | `minValue`, `maxValue`, `value`（初始值）|
| `create_toggle` | `label`（文字标签）, `isOn`（初始状态）|
| `ui_set_text` | `text`（新文本内容）|
| `ui_set_rect` | `left/right/top/bottom`（padding 偏移）|
| `ui_set_anchor` | `preset`（TopLeft/MiddleCenter/StretchAll 等）|
| `ui_layout_children` | `layout`（Vertical/Horizontal/Grid）, `spacing` |

---

## 常用 Prefab 骨架模板

### 全屏窗口骨架（FullScreen UIWindow）

```json
{
  "tool": "batch_execute",
  "commands": [
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "FullScreenUI", "componentType": "Canvas" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "FullScreenUI", "componentType": "CanvasScaler" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "FullScreenUI", "componentType": "GraphicRaycaster" } },
    { "tool": "manage_ui", "params": { "action": "create_panel", "name": "m_tf_Background", "parent": "FullScreenUI", "width": 1920, "height": 1080, "r": 0, "g": 0, "b": 0, "a": 0.8 } },
    { "tool": "manage_ui", "params": { "action": "create_text", "name": "m_tmp_Title", "parent": "FullScreenUI", "text": "标题", "fontSize": 48, "y": 400 } },
    { "tool": "manage_ui", "params": { "action": "create_button", "name": "m_btn_Close", "parent": "FullScreenUI", "text": "×", "x": 600, "y": 400, "width": 80, "height": 80 } },
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_tf_Content", "parent": "FullScreenUI" } }
  ],
  "failFast": true
}
```

### 弹窗骨架（Popup UIWindow）

```json
{
  "tool": "batch_execute",
  "commands": [
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "PopupUI", "componentType": "Canvas" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "PopupUI", "componentType": "CanvasScaler" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "PopupUI", "componentType": "GraphicRaycaster" } },
    { "tool": "manage_ui", "params": { "action": "create_panel", "name": "m_img_Mask", "parent": "PopupUI", "width": 1920, "height": 1080, "r": 0, "g": 0, "b": 0, "a": 0.5 } },
    { "tool": "manage_ui", "params": { "action": "create_panel", "name": "m_tf_Window", "parent": "PopupUI", "width": 600, "height": 400, "r": 0.2, "g": 0.2, "b": 0.2, "a": 1 } },
    { "tool": "manage_ui", "params": { "action": "create_text", "name": "m_tmp_Title", "parent": "m_tf_Window", "text": "提示", "fontSize": 32, "y": 150 } },
    { "tool": "manage_ui", "params": { "action": "create_text", "name": "m_tmp_Content", "parent": "m_tf_Window", "text": "内容文本", "fontSize": 24 } },
    { "tool": "manage_ui", "params": { "action": "create_button", "name": "m_btn_Confirm", "parent": "m_tf_Window", "text": "确认", "x": 100, "y": -150, "width": 150, "height": 60 } },
    { "tool": "manage_ui", "params": { "action": "create_button", "name": "m_btn_Cancel", "parent": "m_tf_Window", "text": "取消", "x": -100, "y": -150, "width": 150, "height": 60 } }
  ],
  "failFast": true
}
```

### 列表窗口骨架（带 ScrollView）

```json
{
  "tool": "batch_execute",
  "commands": [
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "ListUI", "componentType": "Canvas" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "ListUI", "componentType": "CanvasScaler" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "ListUI", "componentType": "GraphicRaycaster" } },
    { "tool": "manage_ui", "params": { "action": "create_text", "name": "m_tmp_Title", "parent": "ListUI", "text": "列表", "fontSize": 36, "y": 400 } },
    { "tool": "manage_ui", "params": { "action": "create_button", "name": "m_btn_Back", "parent": "ListUI", "text": "返回", "x": -500, "y": 400, "width": 120, "height": 60 } },
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_scroll_List", "parent": "ListUI" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "m_scroll_List", "componentType": "ScrollRect" } },
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_tf_ListContent", "parent": "m_scroll_List" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "m_tf_ListContent", "componentType": "VerticalLayoutGroup" } }
  ],
  "failFast": true
}
```

---

## Prefab headless 编辑（修改已有 Prefab）

不打开 Prefab Stage，直接修改 .prefab 文件（**推荐用于自动化**）：

```json
{
  "tool": "manage_prefabs",
  "params": {
    "action": "modify_contents",
    "prefabPath": "Assets/AssetRaw/UI/Prefabs/BattleMainUI.prefab",
    "target": "m_btn_Back",
    "componentProperties": {
      "RectTransform": {
        "localPosition": { "x": -400, "y": 250, "z": 0 }
      }
    }
  }
}
```

**可修改的内容**（通过 `modify_contents`）：
- `name`：重命名节点
- `setActive`：显示/隐藏
- `tag` / `layer`：标签和层
- `position` / `rotation` / `scale`：Transform
- `parent`：更改父节点（Prefab 内部）
- `componentsToAdd` / `componentsToRemove`：增减组件
- `componentProperties`：设置组件属性
- `createChild`：在 Prefab 内创建子节点（支持数组，批量创建）

**查看 Prefab 现有层级**：
```json
{
  "tool": "manage_prefabs",
  "params": {
    "action": "get_hierarchy",
    "prefabPath": "Assets/AssetRaw/UI/Prefabs/BattleMainUI.prefab"
  }
}
```
