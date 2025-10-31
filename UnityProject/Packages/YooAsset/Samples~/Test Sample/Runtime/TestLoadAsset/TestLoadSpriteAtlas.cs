using System;
using System.IO;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;
using UnityEngine.TestTools;
using NUnit.Framework;
using YooAsset;

public class TestLoadSpriteAtlas
{
    public IEnumerator RuntimeTester()
    {
        ResourcePackage package = YooAssets.GetPackage(TestDefine.AssetBundlePackageName);
        Assert.IsNotNull(package);

        var assetHandle = package.LoadAssetAsync<SpriteAtlas>("atlas_icon");
        yield return assetHandle;
        Assert.AreEqual(EOperationStatus.Succeed, assetHandle.Status);

        var spriteAtals = assetHandle.AssetObject as SpriteAtlas;
        Assert.IsNotNull(spriteAtals);

        var sprite1 = spriteAtals.GetSprite("bullet");
        Assert.IsNotNull(sprite1);

        var sprite2 = spriteAtals.GetSprite("pause");
        Assert.IsNotNull(sprite2);

        var sprite3 = spriteAtals.GetSprite("rocket");
        Assert.IsNotNull(sprite3);
    }
}