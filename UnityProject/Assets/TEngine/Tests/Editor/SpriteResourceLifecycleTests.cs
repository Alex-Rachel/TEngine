using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.U2D;
using UnityEngine.UI;
using YooAsset;
using Object = UnityEngine.Object;

public sealed class SpriteResourceLifecycleTests
{
    private const string SceneKey = "TEngine.SpriteResourceLifecycleTests.Scene";

    [UnityTest]
    public IEnumerator UsageScanReleasesClearedReplacedAndDestroyedTargets() => RunInPlayMode(0);

    [UnityTest]
    public IEnumerator SharedLoadingSurvivesCancellationAndInvalidSpriteNames() => RunInPlayMode(1);

    [UnityTest]
    public IEnumerator LatestRequestWinsAcrossBothApisAndShutdownReleasesClones() =>
        RunInPlayMode(2);

    [Test]
    public void PublicSetSubSpriteKeepsPositionalCancellationAndEmptyInputGuards()
    {
        Image image = null;
        SpriteRenderer renderer = null;
        image.SetSprite(string.Empty, false, null, CancellationToken.None);
        renderer.SetSprite(string.Empty, null, CancellationToken.None);
        image.SetSubSprite(string.Empty, "icon", false, CancellationToken.None);
        image.SetSubSprite("atlas", string.Empty, false, CancellationToken.None);
        renderer.SetSubSprite(string.Empty, "icon", CancellationToken.None);
        renderer.SetSubSprite("atlas", string.Empty, CancellationToken.None);
    }

    private static IEnumerator RunInPlayMode(int scenario)
    {
        SpriteTestSceneState.Capture(SceneKey);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        try
        {
            yield return new EnterPlayMode();
            yield return RunScenario(scenario).ToCoroutine();
            yield return new ExitPlayMode();
        }
        finally
        {
            if (!EditorApplication.isPlaying)
                SpriteTestSceneState.Restore(SceneKey);
        }
    }

    private static async UniTask RunScenario(int scenario)
    {
        using var fixture = new ResourceFixture();
        if (scenario == 0)
        {
            var first = fixture.CreateSprite("first");
            var second = fixture.CreateSprite("second");
            var provider = fixture.AddResource("shared.png", typeof(Sprite), first, second);
            fixture.Complete(provider);
            var left = fixture.CreateImage();
            var right = fixture.CreateImage();
            var renderer = left.gameObject.AddComponent<SpriteRenderer>();

            await fixture.SetSubSprite(left, "shared.png", "first");
            await fixture.SetSubSprite(right, "shared.png", "second");
            await fixture.SetSubSprite(renderer, "shared.png", "first");
            var handle = fixture.Handle("shared.png");
            Assert.AreEqual(
                1,
                ResourceFixture.Get(provider, "RefCount"),
                "三个目标应共享一个句柄。"
            );
            Assert.AreEqual(3, fixture.References("shared.png"));
            await fixture.SetSubSprite(left, "shared.png", "first");
            Assert.AreEqual(4, fixture.References("shared.png"));

            left.sprite = null;
            fixture.Scan();
            Assert.AreEqual(2, fixture.References("shared.png"));
            right.gameObject.SetActive(false);
            right.enabled = false;
            fixture.Scan();
            Assert.AreEqual(2, fixture.References("shared.png"), "隐藏和禁用应保留图片。");

            renderer.sprite = second;
            fixture.Scan();
            Assert.AreEqual(
                1,
                fixture.References("shared.png"),
                "同物体的两个组件应独立归还引用。"
            );
            Object.Destroy(right.gameObject);
            await UniTask.DelayFrame(2);
            fixture.Scan();
            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(0, fixture.AssetCount);
        }
        else if (scenario == 1)
        {
            var sprite = fixture.CreateSprite("shared");
            var provider = fixture.AddResource("pending.png", typeof(Sprite), sprite);
            var first = fixture.CreateImage();
            var second = fixture.CreateImage();
            var third = fixture.CreateImage();
            using var cancellation = new CancellationTokenSource();
            var firstLoad = fixture.SetSubSprite(first, "pending.png", "shared");
            var secondLoad = fixture.SetSubSprite(second, "pending.png", "shared");
            var cancelledLoad = fixture.SetSubSprite(
                third,
                "pending.png",
                "shared",
                cancellation.Token
            );
            var handle = fixture.Handle("pending.png");
            cancellation.Cancel();
            await cancelledLoad;
            Assert.IsTrue(handle.IsValid, "一个等待者取消不能释放其他等待者的句柄。");
            Assert.AreEqual(2, fixture.References("pending.png"));
            Object.Destroy(first.gameObject);
            await UniTask.DelayFrame(2);
            fixture.Complete(provider);
            await UniTask.WhenAll(firstLoad, secondLoad);
            Assert.AreSame(sprite, second.sprite);
            Assert.AreEqual(1, fixture.References("pending.png"));

            var failed = false;
            try
            {
                await fixture.SetSubSprite(second, "pending.png", "missing");
            }
            catch (GameFrameworkException)
            {
                failed = true;
            }
            Assert.IsTrue(failed);
            Assert.AreSame(sprite, second.sprite, "失败应保留原图片。");
            Assert.AreEqual(1, fixture.References("pending.png"));

            var ordinary = fixture.CreateSprite("ordinary");
            fixture.CacheOrdinary("ordinary", ordinary);
            second.SetSprite("ordinary");
            fixture.Scan();
            Assert.AreSame(ordinary, second.sprite);
            Assert.IsFalse(handle.IsValid, "切成普通图片后也应归还旧图集。");

            var broken = fixture.AddResource("broken.png", typeof(Sprite), sprite);
            var brokenLoad = fixture.SetSubSprite(second, "broken.png", "shared");
            var brokenHandle = fixture.Handle("broken.png");
            fixture.Fail(broken);
            failed = false;
            try
            {
                await brokenLoad;
            }
            catch (GameFrameworkException)
            {
                failed = true;
            }
            Assert.IsTrue(failed);
            Assert.AreSame(ordinary, second.sprite);
            Assert.IsFalse(brokenHandle.IsValid);
            Assert.AreEqual(0, fixture.AssetCount);
        }
        else if (scenario == 2)
        {
            var image = fixture.CreateImage();
            var slow = fixture.AddResource(
                "slow.png",
                typeof(Sprite),
                fixture.CreateSprite("slow")
            );
            var fastSprite = fixture.CreateSprite("fast");
            var fast = fixture.AddResource("fast.png", typeof(Sprite), fastSprite);
            var oldLoad = fixture.SetSubSprite(image, "slow.png", "slow");
            var newLoad = fixture.SetSubSprite(image, "fast.png", "fast");
            fixture.Complete(fast);
            await newLoad;
            fixture.Complete(slow);
            await oldLoad;
            Assert.AreSame(fastSprite, image.sprite);

            var replaced = fixture.AddResource(
                "replaced.png",
                typeof(Sprite),
                fixture.CreateSprite("replaced")
            );
            var replacedLoad = fixture.SetSubSprite(image, "replaced.png", "replaced");
            var ordinary = fixture.CreateSprite("ordinary");
            fixture.CacheOrdinary("ordinary", ordinary);
            image.SetSprite("ordinary");
            fixture.Complete(replaced);
            await replacedLoad;
            Assert.AreSame(ordinary, image.sprite, "普通图片请求也必须替换旧子图请求。");

            fixture.BlockOrdinary("blocked");
            image.SetSprite("blocked");
            await fixture.SetSubSprite(image, "fast.png", "fast");
            fixture.UnblockOrdinary("blocked");
            await UniTask.DelayFrame(2);
            Assert.AreSame(fastSprite, image.sprite, "子图请求也必须替换旧普通图片请求。");

            var atlasProvider = fixture.AddResource("cached.spriteatlas", typeof(SpriteAtlas));
            ResourceFixture.Set(atlasProvider, "AssetObject", fixture.Track(new SpriteAtlas()));
            var clone = fixture.CreateSprite("clone");
            var atlasLoad = fixture.SetSubSprite(image, "cached.spriteatlas", "clone");
            // 注入已取得的克隆，验证实际句柄/目标生命周期，不依赖图集构建或磁盘 IO。
            fixture.CacheAtlasSprite("cached.spriteatlas", clone);
            fixture.Complete(atlasProvider);
            await atlasLoad;
            var atlasHandle = fixture.Handle("cached.spriteatlas");
            var pending = fixture.AddResource(
                "shutdown.png",
                typeof(Sprite),
                fixture.CreateSprite("pending")
            );
            var pendingLoad = fixture.SetSubSprite(
                fixture.CreateImage(),
                "shutdown.png",
                "pending"
            );
            var pendingHandle = fixture.Handle("shutdown.png");
            fixture.ClosePool();
            fixture.DestroyComponent();
            await UniTask.DelayFrame(2);
            await pendingLoad;
            fixture.Complete(pending);
            Assert.IsFalse(atlasHandle.IsValid);
            Assert.IsFalse(pendingHandle.IsValid);
            Assert.IsTrue(clone == null, "组件销毁必须销毁缓存的图集克隆。");
            Assert.AreEqual(0, fixture.AssetCount);
        }
    }

    // 使用真实 Unity 组件和 YooAsset 句柄；仅通过内存 Provider 控制加载完成时序。
    private sealed class ResourceFixture : IDisposable
    {
        private const BindingFlags Flags =
            BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.NonPublic;
        private readonly Type _componentType = typeof(IResourceModule).Assembly.GetType(
            "TEngine.ResourceExtComponent"
        );
        private readonly Assembly _yooAssembly = typeof(YooAssets).Assembly;
        private readonly List<Object> _objects = new List<Object>();
        private readonly ResourcePackage _previousPackage;
        private readonly ResourcePackage _package;
        private readonly object _manager;
        private readonly IDictionary _assetPaths;
        private readonly IDictionary _locations;
        private readonly IObjectPool<AssetItemObject> _pool;
        private readonly MonoBehaviour _component;
        private readonly IDictionary _subAssets;

        public ResourceFixture()
        {
            _previousPackage = (ResourcePackage)
                typeof(YooAssets).GetField("_defaultPackage", Flags).GetValue(null);
            _package = (ResourcePackage)New("ResourcePackage", "SpriteResourceTests");
            _manager = New("ResourceManager", _package.PackageName);
            Set(_package, "_resourceManager", _manager);
            Set(_package, "_initializeStatus", EOperationStatus.Succeed);
            var manifest = New("PackageManifest");
            Set(manifest, "PackageName", _package.PackageName);
            _assetPaths = (IDictionary)
                Activator.CreateInstance(
                    typeof(Dictionary<,>).MakeGenericType(
                        typeof(string),
                        _yooAssembly.GetType("YooAsset.PackageAsset")
                    )
                );
            _locations = new Dictionary<string, string>();
            Set(manifest, "AssetDic", _assetPaths);
            Set(manifest, "AssetPathMapping1", _locations);
            var playMode = New("PlayModeImpl", _package.PackageName, EPlayMode.EditorSimulateMode);
            Set(playMode, "ActiveManifest", manifest);
            Set(_package, "_playModeImpl", playMode);
            YooAssets.SetDefaultPackage(_package);

            _component = (MonoBehaviour)
                Track(new GameObject("ResourceExt test")).AddComponent(_componentType);
            _component.enabled = false;
            _pool = ModuleSystem
                .GetModule<IObjectPoolModule>()
                .CreateMultiSpawnObjectPool<AssetItemObject>("SpriteResourceTests");
            Set(_component, "_assetItemPool", _pool);
            Set(_component, "_loadAssetObjectsLinkedList", new LinkedList<LoadAssetObject>());
            _componentType.GetProperty("Instance", Flags).SetValue(null, _component);
            _subAssets = (IDictionary)Get(_component, "_subSpriteAssets");
        }

        public int AssetCount => _subAssets.Count;

        public int References(string location) => (int)Get(_subAssets[location], "References");

        public HandleBase Handle(string location) =>
            (HandleBase)Get(_subAssets[location], "Handle");

        public void Scan() =>
            _componentType.GetMethod("ReleaseUnused", Flags).Invoke(_component, null);

        public void BlockOrdinary(string location) =>
            ((HashSet<string>)Get(_component, "_assetLoadingList")).Add(location);

        public void UnblockOrdinary(string location) =>
            ((HashSet<string>)Get(_component, "_assetLoadingList")).Remove(location);

        public void DestroyComponent() => Object.Destroy(_component.gameObject);

        public void ClosePool() =>
            ModuleSystem.GetModule<IObjectPoolModule>().DestroyObjectPool(_pool);

        public void CacheOrdinary(string location, Sprite sprite) =>
            _pool.Register(AssetItemObject.Create(location, sprite), false);

        public void CacheAtlasSprite(string location, Sprite sprite) =>
            ((Dictionary<string, Sprite>)Get(_subAssets[location], "Sprites")).Add(
                sprite.name,
                sprite
            );

        public UniTask SetSubSprite(
            Component target,
            string location,
            string name,
            CancellationToken token = default
        )
        {
            var setAssetObject = target is Image image
                ? SetSpriteObject.Create(image, location, cancellationToken: token)
                : SetSpriteObject.Create(
                    (SpriteRenderer)target,
                    location,
                    cancellationToken: token
                );
            return (UniTask)
                _componentType
                    .GetMethod("SetSubSprite")
                    .Invoke(_component, new object[] { setAssetObject, name, token });
        }

        public object AddResource(string location, Type type, params Sprite[] sprites)
        {
            var asset = New("PackageAsset");
            Set(asset, "AssetPath", location);
            _assetPaths.Add(location, asset);
            _locations.Add(location, location);
            var info = _package.GetAssetInfo(location, type);
            var provider = New("CompletedProvider", _manager, info);
            Set(provider, "SubAssetObjects", sprites);
            Set(provider, "Status", EOperationStatus.Processing);
            var method = type == typeof(SpriteAtlas) ? "LoadAssetAsync" : "LoadSubAssetsAsync";
            ((IDictionary)Get(_manager, "ProviderDic")).Add(method + Get(info, "GUID"), provider);
            return provider;
        }

        public void Complete(object provider) =>
            provider
                .GetType()
                .GetMethod("InvokeCompletion", Flags)
                .Invoke(provider, new object[] { string.Empty, EOperationStatus.Succeed });

        public void Fail(object provider) =>
            provider
                .GetType()
                .GetMethod("InvokeCompletion", Flags)
                .Invoke(provider, new object[] { "fixture failure", EOperationStatus.Failed });

        public Image CreateImage() =>
            Track(new GameObject("Sprite target", typeof(RectTransform))).AddComponent<Image>();

        public Sprite CreateSprite(string name)
        {
            var texture = Track(new Texture2D(4, 4));
            var sprite = Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f));
            sprite.name = name;
            return sprite;
        }

        public T Track<T>(T value)
            where T : Object
        {
            _objects.Add(value);
            return value;
        }

        private object New(string name, params object[] args) =>
            Activator.CreateInstance(
                _yooAssembly.GetType("YooAsset." + name),
                Flags,
                null,
                args,
                null
            );

        public static object Get(object target, string name)
        {
            var field = target.GetType().GetField(name, Flags);
            return field != null
                ? field.GetValue(target)
                : target.GetType().GetProperty(name, Flags).GetValue(target);
        }

        public static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, Flags);
            if (field != null)
                field.SetValue(target, value);
            else
                target.GetType().GetProperty(name, Flags).SetValue(target, value);
        }

        public void Dispose()
        {
            if (_component != null)
                Object.DestroyImmediate(_component.gameObject);
            ModuleSystem.GetModule<IObjectPoolModule>().DestroyObjectPool(_pool);
            YooAssets.SetDefaultPackage(_previousPackage);
            for (var i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null)
                    Object.DestroyImmediate(_objects[i]);
        }
    }
}
