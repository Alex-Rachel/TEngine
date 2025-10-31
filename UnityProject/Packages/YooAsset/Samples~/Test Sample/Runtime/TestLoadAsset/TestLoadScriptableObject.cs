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

public class TestLoadScriptableObject
{
    public IEnumerator RuntimeTester()
    {
        ResourcePackage package = YooAssets.GetPackage(TestDefine.AssetBundlePackageName);
        Assert.IsNotNull(package);

        // 异步加载序列化对象
        {
            var assetHandle = package.LoadAssetAsync("config_a");
            yield return assetHandle;
            Assert.AreEqual(EOperationStatus.Succeed, assetHandle.Status);

            var testScriptableObject = assetHandle.AssetObject as TestScriptableObject;
            Assert.IsNotNull(testScriptableObject);
            TestLogger.Log(this, testScriptableObject.ConfigName);
        }

        // 同步加载序列化对象
        {
            var assetHandle = package.LoadAssetSync("config_b");
            yield return assetHandle;
            Assert.AreEqual(EOperationStatus.Succeed, assetHandle.Status);

            var testScriptableObject = assetHandle.AssetObject as TestScriptableObject;
            Assert.IsNotNull(testScriptableObject);
            TestLogger.Log(this, testScriptableObject.ConfigName);
        }
    }
}