using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

public class SceneHome : MonoBehaviour
{
    public GameObject CanvasDesktop;
    private AssetHandle _windowHandle;

    private IEnumerator Start()
    {
        // 加载主页面
        _windowHandle = YooAssets.LoadAssetAsync<GameObject>("UIHome");
        yield return _windowHandle;
        _windowHandle.InstantiateSync(CanvasDesktop.transform);

        // 切换场景的时候释放资源
        var package = YooAssets.GetPackage("DefaultPackage");
        var operation = package.UnloadUnusedAssetsAsync();
        yield return operation;
    }

    private void OnDestroy()
    {
        // 释放资源句柄
        if (_windowHandle != null)
        {
            _windowHandle.Release();
            _windowHandle = null;
        }
    }
}