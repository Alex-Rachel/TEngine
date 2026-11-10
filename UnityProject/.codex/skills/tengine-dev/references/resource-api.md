# 资源 API 与所有权

权威签名：[IResourceModule](../../../../Assets/TEngine/Runtime/Module/ResourceModule/IResourceModule.cs)、
[ResourceModule](../../../../Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs)、
[SetSpriteExtensions](../../../../Assets/TEngine/Runtime/Module/ResourceModule/Extension/Implement/SetSpriteExtensions.cs)。

| 需求 | API | 释放责任 |
|---|---|---|
| Image/SpriteRenderer 显示图片 | `SetSprite`、`SetSubSprite` | 扩展组件管理；同目标新请求替换旧请求 |
| 实例化 Prefab | `LoadGameObjectAsync` | 销毁实例，由 AssetsReference 释放 |
| 普通 Asset | `LoadAssetAsync<T>` | 每次成功取得后配对 `UnloadAsset` |
| 原始句柄 | `LoadAssetAsyncHandle<T>` | 调用方持有并释放句柄，不能与对象池 API 混用 |
| Image/SpriteRenderer 散图 | `SetSprite` | 扩展组件跟踪目标和资源引用，业务传入适合的取消 token |
| 图集子图 | `SetSubSprite` | 通过 SubSpriteReference 管理；存在下述当前源码限制 |
| 实例化 Prefab | `LoadGameObjectAsync` | 调用方销毁实例，由 AssetsReference 交还源 Prefab 引用 |
| 普通 Asset | `LoadAssetAsync<T>` | 每次成功获取都配对 `UnloadAsset`，包括未接收的过期结果 |
| 原始句柄 | `LoadAssetAsyncHandle<T>` | 调用方等待、检查状态并 Dispose；不再用 UnloadAsset 释放同一所有权 |

`LoadAsset<T>`、`LoadGameObject` 等同步 API 确实存在。业务大资源优先异步，启动层和 Editor 工具按实际职责选择。
原始 Handle API 不经 TEngine 对象池，与普通 Asset API 的所有权不同。

```csharp
// Image callback 是 Action<Image>，不是 Action<Sprite>。
image.SetSprite(location, setNativeSize: true, cancellationToken: token);
// SetSubSprite 没有 callback 重载。
image.SetSubSprite(atlasLocation, spriteName, cancellationToken: token);
```

普通资源的短期消费片段：

```csharp
var asset = await GameModule.Resource.LoadAssetAsync<TextAsset>(location, token);
try
{
    if (asset == null || token.IsCancellationRequested) return;
    Consume(asset.text);
}
finally
{
    if (asset != null) GameModule.Resource.UnloadAsset(asset);
}
```

上述为嵌入业务的片段；`Consume`、`token`、地址由调用方提供。
`SetSubSprite` 支持 SpriteAtlas 与纹理切片，共享句柄并管理图集 Sprite 克隆。清空、换图或销毁目标后，既有使用扫描归还引用；隐藏但保留图片时继续持有。两种图片入口共用请求替换状态，取消或失败保留当前图片；直接清空图片不会取消未完成请求。
取消可能返回 null，也可能抛异常，核实当前实现，不能只捕获 `OperationCanceledException`。
异步后的所有权转移见 [resource-patterns.md](resource-patterns.md)。
片段中的 `Consume`、token 和地址由业务提供；异常由所属异步调用链观察。
`UnloadAsset` 在 [ResourceModule.Pool](../../../../Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Pool.cs) 中仅 Unspawn，
底层句柄由 [AssetObject.Release](../../../../Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.AssetObject.cs) 在池回收时 Dispose。
交还引用不等于立即卸载 Bundle；`UnloadUnusedAssets` 也不替代配对释放。

`location` 取决于当前 YooAsset 收集器的 AddressRule，不保证总是文件名，也不保证任意路径都有效。
先检查 `CheckLocationValid(location, packageName)` 和收集配置；不要靠去掉扩展名“修复”无效地址。
实例资源的关联实现见 [AssetsReference](../../../../Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsReference.cs)，
不要额外 UnloadAsset 同一份已转移给实例的引用。

## 当前源码限制

- `LoadAssetAsync`、`LoadGameObjectAsync` 的缓存分支和同地址等待并不完整遵循传入 token。
  某些取消路径返回 null，加载/类型/初始化错误仍可能抛异常；await 返回后检查状态和所有权，见 [资源模式](resource-patterns.md)。
- 散图 [SetAssetByResources](../../../../Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs)
  已按目标替换旧请求并检查当前请求身份，不需要机械复制另一套相同防覆盖逻辑。
  跨业务步骤的状态和隐藏策略仍由业务负责。
- 子图 [SetSubSprite](../../../../Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.SubSprite.cs)
  未复用散图请求替换机制；同一对象反复设置同一 location 会累计引用，而销毁仅减少一次。
  高频复用和并发刷新需要针对当前实现验证，不能宣称自动释放已覆盖这些情况。

以上是现存实现的边界，不表示本轮已修复。全包强制卸载和缓存删除需单独授权。
