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
using UnityEngine.Video;

public class TestLoadVideo
{
    public IEnumerator RuntimeTester()
    {
        ResourcePackage package = YooAssets.GetPackage(TestDefine.RawBundlePackageName);
        Assert.IsNotNull(package);

        var rawFileHandle = package.LoadRawFileAsync("video_logo");
        yield return rawFileHandle;
        Assert.AreEqual(EOperationStatus.Succeed, rawFileHandle.Status);

        // 获取视频文件地址
        string videoFilePath = rawFileHandle.GetRawFilePath();
        Assert.IsTrue(File.Exists(videoFilePath));

        // 创建预制体播放视频
        GameObject go = new GameObject("video player");
        var videoPlayer = go.AddComponent<VideoPlayer>(); 
        videoPlayer.source = VideoSource.Url;
        videoPlayer.renderMode = VideoRenderMode.APIOnly;
        videoPlayer.url = videoFilePath;
        videoPlayer.Play();

        yield return new WaitForSeconds(1f);
        Assert.IsTrue(videoPlayer.isPlaying);
    }
}