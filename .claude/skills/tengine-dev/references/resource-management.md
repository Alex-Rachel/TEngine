# TEngine 资源管理

## 目录

- [核心原则](#核心原则)
- [资源寻址方式](#资源寻址方式)
- [Sprite 加载（SetSprite 扩展，推荐）](#sprite-加载setsprite-扩展推荐)
- [GameObject 加载（自动引用计数）](#gameobject-加载自动引用计数)
- [其他 Asset 加载](#其他-asset-加载)
- [资源卸载](#资源卸载)
- [资源信息查询](#资源信息查询)
- [生命周期管理模式](#生命周期管理模式)
- [常见场景代码](#常见场景代码)

---

## 核心原则

1. **禁止 `Resources.Load()`**：所有资源通过 YooAsset 加载，放在 `Assets/AssetRaw/` 下
2. **Sprite 用 `SetSprite` 扩展方法**：内置缓存池管理，无需手动 `UnloadAsset`
3. **GameObject 用 `LoadGameObject / LoadGameObjectAsync`**：自动管理引用计数，`Destroy` 时自动卸载
4. **其他 Asset 加载/释放必须配对**：`LoadAssetAsync<T>` → 用完后 → `UnloadAsset`
5. **异步优先**：禁止在主线程同步加载大资源，始终使用 `await`
6. **避免静态引用**：不要将 Asset 存入静态变量，防止无法卸载

**加载方式选择**：

| 资源类型 | 推荐 API | 是否需要手动释放 |
|---------|---------|---------------|
| Sprite / 图集子图 | `SetSprite` / `SetSubSprite` | 否（缓存池自动管理）|
| 需实例化的 Prefab | `LoadGameObjectAsync` | 否（Destroy 时自动）|
| TextAsset / SO 等其他 | `LoadAssetAsync<T>` | **是**，用完必须 `UnloadAsset` |

---

## 资源寻址方式

YooAsset 通过 **location**（资源地址）寻址，等于资源文件名（不含路径和扩展名）：

```
文件路径：Assets/AssetRaw/UI/Prefabs/BattleMainUI.prefab  →  location：BattleMainUI
文件路径：Assets/AssetRaw/Audios/BGM/MainTheme.mp3        →  location：MainTheme
```

如有同名文件在不同子目录，可使用相对路径去重：`UI/BattleMainUI`。

---

## Sprite 加载（SetSprite 扩展，推荐）

框架内置缓存池自动管理引用计数，**无需手动 `UnloadAsset`**。

```csharp
// Image 组件（最常用）
_imgIcon.SetSprite("item_icon_001");
_imgIcon.SetSprite("item_icon_001", setNativeSize: true);
_imgIcon.SetSprite("item_icon_001", callback: img => img.color = Color.white);
_imgIcon.SetSprite("item_icon_001", cancellationToken: _cts.Token);

// SpriteRenderer 组件
_spriteRenderer.SetSprite("hero_sprite");

// 图集子图片（Sprite Atlas）
_imgIcon.SetSubSprite("ItemAtlas", "item_sword_01");
_spriteRenderer.SetSubSprite("HeroAtlas", "hero_idle_01");
```

**禁止**用 `LoadAssetAsync<Sprite>` 加载图片（无缓存池且需手动释放）：

```csharp
// ❌ 不要这样做
var sprite = await GameModule.Resource.LoadAssetAsync<Sprite>("item_icon_001");
// ✅ 正确做法
_imgIcon.SetSprite("item_icon_001");
```

---

## GameObject 加载（自动引用计数）

框架自动管理引用计数，`Destroy` 时自动卸载，**无需手动 `UnloadAsset`**。

```csharp
// 同步加载（需资源已预加载）
GameObject go = GameModule.Resource.LoadGameObject("HeroPrefab");
GameObject go = GameModule.Resource.LoadGameObject("HeroPrefab", parentTransform);

// 异步加载（推荐）
GameObject go = await GameModule.Resource.LoadGameObjectAsync("HeroPrefab");
GameObject go = await GameModule.Resource.LoadGameObjectAsync("HeroPrefab", parentTransform);
GameObject go = await GameModule.Resource.LoadGameObjectAsync("HeroPrefab", parentTransform, cancellationToken);

// 回收：直接 Destroy，框架自动归还引用计数
Destroy(go);
```

**禁止** `LoadAssetAsync<GameObject>` + `Instantiate` 组合：

```csharp
// ❌ 不要这样做
var prefab = await GameModule.Resource.LoadAssetAsync<GameObject>("HeroPrefab");
var go = Instantiate(prefab); // 还需手动追踪并 UnloadAsset(prefab)
// ✅ 正确做法
var go = await GameModule.Resource.LoadGameObjectAsync("HeroPrefab", parent);
```

---

## 其他 Asset 加载

**仅用于非 Sprite / 非 GameObject 的 Asset（TextAsset、ScriptableObject 等）。用完后必须调用 `UnloadAsset`。**

### 同步加载（需资源已在内存中）

```csharp
T asset = GameModule.Resource.LoadAsset<T>("location");
```

### 异步加载（推荐）

```csharp
// await 风格
var config = await GameModule.Resource.LoadAssetAsync<TextAsset>("level_data");
// 用完后释放
GameModule.Resource.UnloadAsset(config);
```

### 回调风格

```csharp
var callbacks = new LoadAssetCallbacks(
    onSuccess: (location, asset, duration, userData) => { /* 使用 asset */ },
    onFailure: (location, status, errorMessage, userData) =>
        Log.Error($"加载失败: {location}, {errorMessage}")
);
GameModule.Resource.LoadAssetAsync("location", priority: 100, callbacks, userData: null);
```

### 获取原始 Handle（高级用法）

```csharp
AssetHandle handle = GameModule.Resource.LoadAssetAsyncHandle<T>("location");
await handle.Task;
T asset = handle.GetAssetObject<T>();
handle.Release(); // 手动释放
```

---

## 资源卸载

```csharp
// 卸载指定 Asset（引用计数-1，为0时真正卸载）
GameModule.Resource.UnloadAsset(asset);

// 卸载引用计数为0的资源（主动整理内存）
GameModule.Resource.UnloadUnusedAssets();

// 场景切换后强制卸载未使用资源（通常配合 GC）
GameModule.Resource.ForceUnloadUnusedAssets(gcCollection: true);

// 强制卸载所有资源（慎用，仅退出游戏时）
GameModule.Resource.ForceUnloadAllAssets();
```

**引用计数生命周期**：
```
LoadAssetAsync → 引用计数+1 → 使用 → UnloadAsset → 引用计数-1 → (=0) → 真正卸载
```

---

## 资源信息查询

```csharp
// 校验地址是否合法
bool valid = GameModule.Resource.CheckLocationValid("location");

// 检查资源状态
HasAssetResult result = GameModule.Resource.HasAsset("location");
// Valid        → 在本地缓存，可直接加载
// NotExist     → 不存在
// AssetOnline  → 在远端，需下载
// AssetOnDisk  → 在本地磁盘（已下载未解压）

// 获取单个资源信息
AssetInfo info = GameModule.Resource.GetAssetInfo("location");
string address = info.Address;
string tag     = info.Tag;

// 按标签批量获取
AssetInfo[] infos = GameModule.Resource.GetAssetInfos("tag");
```

---

## 生命周期管理模式

### 模式一：UI 中加载 Sprite（无需释放）

```csharp
public class ItemDetailUI : UIWindow
{
    protected override void OnCreate()
    {
        _imgIcon.SetSprite("item_icon_001");
        _imgBg.SetSprite("item_bg_common", setNativeSize: true);
        // OnDestroy 无需做任何资源释放
    }
}
```

### 模式二：UI 中加载 GameObject

```csharp
public class BattleUI : UIWindow
{
    private GameObject _heroModel;

    protected override async UniTaskVoid OnCreate()
    {
        _heroModel = await GameModule.Resource.LoadGameObjectAsync("HeroModel", _transModelRoot);
    }

    protected override void OnDestroy()
    {
        if (_heroModel != null)
            Destroy(_heroModel); // 自动归还引用计数
    }
}
```

### 模式三：加载其他 Asset（必须手动释放）

```csharp
public class LoadingUI : UIWindow
{
    private TextAsset _configAsset;
    private CancellationTokenSource _cts;

    protected override void OnCreate()
    {
        _cts = new CancellationTokenSource();
        LoadAsync(_cts.Token).Forget();
    }

    private async UniTaskVoid LoadAsync(CancellationToken token)
    {
        try
        {
            _configAsset = await GameModule.Resource.LoadAssetAsync<TextAsset>("level_data", token);
            ParseConfig(_configAsset.text);
        }
        catch (OperationCanceledException) { }
    }

    protected override void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        if (_configAsset != null)
            GameModule.Resource.UnloadAsset(_configAsset); // 必须释放
    }
}
```

---

## 常见场景代码

### 动态设置图标

```csharp
_imgIcon.SetSprite("item_icon_sword");
_imgIcon.SetSprite("item_icon_sword", setNativeSize: true);
_imgIcon.SetSubSprite("ItemAtlas", "icon_sword_01"); // 图集子图片
```

### 动态加载并实例化 Prefab

```csharp
var effect = await GameModule.Resource.LoadGameObjectAsync("HitEffect", transform);
Destroy(effect); // 播放完后 Destroy，自动卸载
```

### 预加载一批资源

```csharp
AssetInfo[] preloadInfos = GameModule.Resource.GetAssetInfos("PRELOAD");
var tasks = preloadInfos.Select(info =>
    GameModule.Resource.LoadAssetAsync<Object>(info.Address).AsUniTask()
);
await UniTask.WhenAll(tasks);
```

### 检查资源是否需要下载

```csharp
if (GameModule.Resource.HasAsset("HeroPrefab") == HasAssetResult.AssetOnline)
    Log.Warning("资源需要联网下载");
```
