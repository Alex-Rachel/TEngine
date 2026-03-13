# 场景、GameObject 与组件操作（unity-mcp）

## 目录
- [manage_scene：场景管理](#manage_scene场景管理)
- [manage_gameobject：GameObject CRUD](#manage_gameobjectgameobject-crud)
- [find_gameobjects：查找对象](#find_gameobjects查找对象)
- [manage_components：组件管理](#manage_components组件管理)
- [TEngine 场景约定](#tengine-场景约定)

---

## manage_scene：场景管理

### 查询当前场景信息
```json
{ "tool": "manage_scene", "params": { "action": "get_active" } }
```
返回：`name`、`path`、`isDirty`、`buildIndex`、`rootCount`

### 获取场景层级（分页）
```json
{
  "tool": "manage_scene",
  "params": {
    "action": "get_hierarchy",
    "pageSize": 50,
    "cursor": 0,
    "includeTransform": true
  }
}
```
- `parent`：指定父节点名/ID，查询其子节点
- `maxDepth`：最大层级深度
- 返回结果含 `next_cursor`，若 `truncated=true` 则需继续翻页

### 获取指定父节点的子节点
```json
{
  "tool": "manage_scene",
  "params": {
    "action": "get_hierarchy",
    "parent": "UIRoot",
    "pageSize": 100
  }
}
```

### 保存当前场景
```json
{ "tool": "manage_scene", "params": { "action": "save" } }
```

### 加载场景（编辑器）
```json
{
  "tool": "manage_scene",
  "params": {
    "action": "load",
    "name": "GameScene",
    "path": "Assets/Scenes"
  }
}
```
> **注意**：加载场景前如有未保存改动会报错，需先 `save`。

### 创建新场景
```json
{
  "tool": "manage_scene",
  "params": {
    "action": "create",
    "name": "BattleScene",
    "path": "Assets/Scenes"
  }
}
```

### 截图（保存到 Assets/Screenshots/）
```json
{
  "tool": "manage_scene",
  "params": {
    "action": "screenshot",
    "fileName": "battle_result",
    "superSize": 2
  }
}
```

### 查看 Build Settings 中的场景列表
```json
{ "tool": "manage_scene", "params": { "action": "get_build_settings" } }
```

---

## manage_gameobject：GameObject CRUD

### 创建 GameObject
```json
{
  "tool": "manage_gameobject",
  "params": {
    "action": "create",
    "name": "SpawnPoint",
    "parent": "LevelRoot",
    "position": { "x": 0, "y": 1, "z": 5 },
    "tag": "Respawn",
    "layer": "Default"
  }
}
```
- `componentType`：同时挂载的组件类型（如 `Canvas`、`Light`）
- `primitiveType`：创建内置几何体（Cube/Sphere/Plane/Cylinder/Capsule/Quad）

```json
{
  "tool": "manage_gameobject",
  "params": {
    "action": "create",
    "name": "Floor",
    "primitiveType": "Plane",
    "position": { "x": 0, "y": 0, "z": 0 },
    "scale": { "x": 10, "y": 1, "z": 10 }
  }
}
```

### 修改 GameObject
```json
{
  "tool": "manage_gameobject",
  "params": {
    "action": "modify",
    "target": "SpawnPoint",
    "position": { "x": 10, "y": 0, "z": 0 },
    "rotation": { "x": 0, "y": 90, "z": 0 },
    "scale": { "x": 2, "y": 2, "z": 2 },
    "setActive": true,
    "tag": "Player",
    "layer": "UI",
    "name": "SpawnPoint_New"
  }
}
```
- `parent`：更改父节点（传空字符串脱离父节点）
- `worldPositionStays`：更改父节点时是否保持世界坐标（默认 true）

### 删除 GameObject
```json
{
  "tool": "manage_gameobject",
  "params": { "action": "delete", "target": "SpawnPoint" }
}
```

### 复制 GameObject
```json
{
  "tool": "manage_gameobject",
  "params": {
    "action": "duplicate",
    "target": "SpawnPoint",
    "name": "SpawnPoint_Copy",
    "position": { "x": 5, "y": 0, "z": 0 }
  }
}
```

### 相对移动（move_relative）
```json
{
  "tool": "manage_gameobject",
  "params": {
    "action": "move_relative",
    "target": "Player",
    "deltaPosition": { "x": 1, "y": 0, "z": 0 },
    "space": "World"
  }
}
```

---

## find_gameobjects：查找对象

### 按名称查找
```json
{
  "tool": "find_gameobjects",
  "params": {
    "searchMethod": "by_name",
    "query": "Enemy",
    "includeInactive": true,
    "maxResults": 50
  }
}
```

### 按路径查找
```json
{
  "tool": "find_gameobjects",
  "params": {
    "searchMethod": "by_path",
    "query": "GameRoot/Enemies/Enemy_01"
  }
}
```

### 按 Tag 查找
```json
{
  "tool": "find_gameobjects",
  "params": {
    "searchMethod": "by_tag",
    "query": "Enemy",
    "maxResults": 100
  }
}
```

### 按 Layer 查找
```json
{
  "tool": "find_gameobjects",
  "params": {
    "searchMethod": "by_layer",
    "query": "Enemy"
  }
}
```

返回结果包含每个 GO 的：`name`、`instanceID`、`path`、`componentTypes`、`activeSelf`

---

## manage_components：组件管理

### 添加组件
```json
{
  "tool": "manage_components",
  "params": {
    "action": "add",
    "target": "Player",
    "componentType": "Rigidbody",
    "properties": {
      "mass": 1.5,
      "useGravity": true,
      "isKinematic": false
    }
  }
}
```
- `componentType` 支持简名（`Rigidbody`）或完整类名（`UnityEngine.Rigidbody`）
- `properties`：添加时顺便设置属性（可选）

### 删除组件
```json
{
  "tool": "manage_components",
  "params": {
    "action": "remove",
    "target": "Player",
    "componentType": "Rigidbody"
  }
}
```

### 设置组件属性（set_property）
```json
{
  "tool": "manage_components",
  "params": {
    "action": "set_property",
    "target": "MainCamera",
    "componentType": "Camera",
    "properties": {
      "fieldOfView": 60,
      "nearClipPlane": 0.1,
      "farClipPlane": 1000,
      "backgroundColor": { "r": 0.1, "g": 0.1, "b": 0.2, "a": 1 }
    }
  }
}
```

### 常用组件类型名速查

| 用途 | componentType 值 |
|------|----------------|
| 刚体 | `Rigidbody` / `Rigidbody2D` |
| 碰撞体 | `BoxCollider` / `SphereCollider` / `CapsuleCollider` / `MeshCollider` |
| 渲染器 | `MeshRenderer` / `SkinnedMeshRenderer` / `SpriteRenderer` |
| 灯光 | `Light` |
| 相机 | `Camera` |
| 音频 | `AudioSource` / `AudioListener` |
| 动画 | `Animator` / `Animation` |
| 导航 | `NavMeshAgent` / `NavMeshObstacle` |
| 粒子 | `ParticleSystem` |
| UI | `Canvas` / `CanvasScaler` / `GraphicRaycaster` / `Button` / `Image` / `Text` |
| 布局 | `VerticalLayoutGroup` / `HorizontalLayoutGroup` / `GridLayoutGroup` / `ContentSizeFitter` |
| 物理材质 | `PhysicMaterial` |

---

## TEngine 场景约定

| 约定 | 说明 |
|------|------|
| **UIRoot** | 场景中必须存在名为 `UIRoot` 的 GameObject，`UIModule.OnInit()` 自动查找 |
| **场景路径** | `Assets/Scenes/` 或 `Assets/AssetRaw/Scenes/` |
| **层级命名** | 通常有 GameRoot → Logic / UI / Effect 三层分类 |
| **禁止在游戏场景中直接放 UI** | 所有 UI 通过 `UIModule.ShowUIAsync` 动态加载，场景中只有 UIRoot |

**查看 UIRoot 的层级结构**（调试用）：
```json
{
  "tool": "manage_scene",
  "params": {
    "action": "get_hierarchy",
    "parent": "UIRoot",
    "pageSize": 100,
    "includeTransform": false
  }
}
```
