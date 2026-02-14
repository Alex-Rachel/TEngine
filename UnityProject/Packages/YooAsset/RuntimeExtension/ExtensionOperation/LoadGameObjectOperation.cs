using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

public static class YooAssetsExtension
{
    public static LoadGameObjectOperation LoadGameObjectAsync(this ResourcePackage resourcePackage, string location, Vector3 position, Quaternion rotation, Transform parent, bool destroyGoOnRelease = false)
    {
        var operation = new LoadGameObjectOperation(location, position, rotation, parent, destroyGoOnRelease);
        YooAssets.StartOperation(operation);
        return operation;
    }
}

public class LoadGameObjectOperation : GameAsyncOperation
{
    private enum ESteps
    {
        None,
        LoadAsset,
        Done,
    }

    private readonly string _location;
    private readonly Vector3 _positon;
    private readonly Quaternion _rotation;
    private readonly Transform _parent;
    private readonly bool _destroyGoOnRelease;
    private AssetHandle _handle;
    private ESteps _steps = ESteps.None;

    /// <summary>
    /// 加载的游戏对象
    /// </summary>
    public GameObject Go { private set; get; }


    public LoadGameObjectOperation(string location, Vector3 position, Quaternion rotation, Transform parent, bool destroyGoOnRelease = false)
    {
        _location = location;
        _positon = position;
        _rotation = rotation;
        _parent = parent;
        _destroyGoOnRelease = destroyGoOnRelease;
    }
    protected override void OnStart()
    {
        _steps = ESteps.LoadAsset;
    }
    protected override void OnUpdate()
    {
        if (_steps == ESteps.None || _steps == ESteps.Done)
            return;

        if (_steps == ESteps.LoadAsset)
        {
            if (_handle == null)
            {
                _handle = YooAssets.LoadAssetAsync<GameObject>(_location);
            }

            Progress = _handle.Progress;
            if (_handle.IsDone == false)
                return;

            if (_handle.Status != EOperationStatus.Succeed)
            {
                Error = _handle.LastError;
                Status = EOperationStatus.Failed;
                _steps = ESteps.Done;
            }
            else
            {
                Go = _handle.InstantiateSync(_positon, _rotation, _parent);
                Status = EOperationStatus.Succeed;
                _steps = ESteps.Done;
            }
        }
    }
    protected override void OnAbort()
    {
    }

    /// <summary>
    /// 释放资源句柄
    /// </summary>
    public void ReleaseHandle()
    {
        if (_handle != null)
        {
            _handle.Release();

            if (_destroyGoOnRelease)
            {
                if (Go != null)
                    GameObject.Destroy(Go);
            }
        }
    }
}