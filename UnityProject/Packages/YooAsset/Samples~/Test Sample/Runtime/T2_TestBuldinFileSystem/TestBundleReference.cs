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

public class TestBundleReference
{
    public IEnumerator RuntimeTester()
    {
        ResourcePackage package = YooAssets.GetPackage(TestDefine.AssetBundlePackageName);
        Assert.IsNotNull(package);

        // 加载HeroA
        {
            var assetHandle = package.LoadAssetAsync<GameObject>("hero_a");
            yield return assetHandle;
            Assert.AreEqual(EOperationStatus.Succeed, assetHandle.Status);

            var pos = new Vector3(-1, -1, 0);
            var go = assetHandle.InstantiateSync(pos, Quaternion.identity);
            Assert.IsNotNull(go);
        }

        // 加载HeroB
        AssetHandle heroHandle;
        GameObject heroObject;
        {
            heroHandle = package.LoadAssetAsync<GameObject>("hero_b");
            yield return heroHandle;
            Assert.AreEqual(EOperationStatus.Succeed, heroHandle.Status);

            var pos = new Vector3(1, -1, 0);
            heroObject = heroHandle.InstantiateSync(pos, Quaternion.identity);
            Assert.IsNotNull(heroObject);
        }

        // 卸载HeroB
        {
            heroHandle.Release();
            GameObject.Destroy(heroObject);
            yield return new WaitForEndOfFrame();
        }

        // 清理未使用资源
        {
            var operation = package.UnloadUnusedAssetsAsync();
            yield return operation;
            Assert.AreEqual(EOperationStatus.Succeed, operation.Status);
        }

        // 再次加载HeroB
        {
            heroHandle = package.LoadAssetAsync<GameObject>("hero_b");
            yield return heroHandle;
            Assert.AreEqual(EOperationStatus.Succeed, heroHandle.Status);

            var pos = new Vector3(1, -1, 0);
            heroObject = heroHandle.InstantiateSync(pos, Quaternion.identity);
            Assert.IsNotNull(heroObject);

            // 检测材质球关联的纹理是否为空
            var mat = heroObject.GetComponent<MeshRenderer>().material;
            Assert.IsNotNull(mat.mainTexture);
        }
    }
}