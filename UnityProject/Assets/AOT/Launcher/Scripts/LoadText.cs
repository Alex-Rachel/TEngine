using UnityEngine;

namespace Launcher
{
    public class TextMode
    {
        public string Label_Load_Progress = "正在下载资源文件，请耐心等待\n当前下载速度：{0}/s 资源文件大小：{1}";
        public string Label_Load_FirstUnpack = "首次进入游戏，正在初始化游戏资源...（此过程不消耗网络流量）";
        public string Label_Load_Unpacking = "正在更新本地资源版本，请耐心等待...（此过程不消耗网络流量）";
        public string Label_Load_Checking = "检测版本文件{0}...";
        public string Label_Load_Checked = "最新版本检测完成";
        public string Label_Load_Package = "当前使用的版本过低，请下载安装最新版本";
        public string Label_Load_Plantform = "当前使用的版本过低，请前往应用商店安装最新版本";
        public string Label_Load_Notice = "检测到可选资源更新，推荐完成更新提升游戏体验";
        public string Label_Load_Force = "检测到版本更新，取消更新将导致无法进入游戏";

        public string Label_Load_Force_WIFI =
            "检测到有新的游戏内容需要更新，更新包大小<color=#BA3026>{0}</color>, 取消更新将导致无法进入游戏，您当前已为<color=#BA3026>wifi网络</color>，请开始更新";

        public string Label_Load_Force_NO_WIFI =
            "检测到有新的游戏内容需要更新，更新包大小<color=#BA3026>{0}</color>, 取消更新将导致无法进入游戏，请开始更新";

        public string Label_Load_Error = "更新参数错误{0}，请点击确定重新启动游戏";
        public string Label_Load_FirstEntrerGame_Error = "首次进入游戏资源异常";
        public string Label_Load_UnpackComplete = "正在加载最新资源文件...（此过程不消耗网络流量）";
        public string Label_Load_UnPackError = "资源解压失败，请点击确定重新启动游戏";
        public string Label_Load_Load_Progress = "正在载入...{0}%";
        public string Label_Load_Download_Progress = "正在下载...{0}%";
        public string Label_Load_Load_Complete = "载入完成";
        public string Label_Load_Init = "初始化...";
        public string Label_Net_UnReachable = "当前网络不可用，请检查本地网络设置后点击确认进行重试";
        public string Label_Net_ReachableViaCarrierDataNetwork = "当前是移动网络，是否继续下载";
        public string Label_Net_Error = "网络异常，请重试";
        public string Label_Net_Changed = "网络切换,正在尝试重连,{0}次";
        public string Label_Data_Empty = "数据异常";
        public string Label_Memory_Low = "初始化资源加载失败，请检查本地内存是否充足";
        public string Label_Memory_Low_Load = "内存是否充足,无法更新";
        public string Label_Memory_UnZip_Low = "内存不足，无法解压";
        public string Label_App_id = "游戏版本号:{0}";
        public string Label_Res_id = "资源版本号:{0}";
        public string Label_Clear_Comfirm = "是否清理本地资源?(清理完成后会关闭游戏且重新下载最新资源)";
        public string Label_RestartApp = "本次更新需要重启应用，请点击确定重新启动游戏";
        public string Label_DownLoadFailed = "网络太慢，是否继续下载";
        public string Label_ClearConfig = "清除环境配置，需要重启应用";
        public string Label_RegionInfoIllegal = "区服信息为空";
        public string Label_RemoteUrlisNull = "热更地址为空";
        public string Label_FirstPackageNotFound = "首包资源加载失败";
        public string Label_RequestReginInfo = "正在请求区服信息{0}次";
        public string Label_RequestTimeOut = "请求区服信息超时,是否重试？";
        public string Label_Region_ArgumentError = "参数错误";
        public string Label_Region_IndexOutOfRange = "索引越界";
        public string Label_Region_NonConfigApplication = "未配置此应用";
        public string Label_Region_SystemError = "系统异常";

        public string Label_PreventionOfAddiction = "著作人权：XX市TEngine有限公司 软著登记号：2022SR0000000\n抵制不良游戏，拒绝盗版游戏。注意自我保护，谨防受骗上当。适度游戏益脑，" +
                                                    "沉迷游戏伤身。合理安排时间，享受健康生活。";

        public string Label_Btn_Update = "确定";
        public string Label_Btn_Ignore = "取消";
        public string Label_Btn_Package = "更新";

        public string Label_Dlc_ConfigVerificateStage = "配置校验中...";
        public string Label_Dlc_ConfigLoadingStage = "下载配置中...";
        public string Label_Dlc_AssetsLoading = "下载资源中...";
        public string Label_Dlc_LoadingFinish = "下载结束";

        public string Label_Dlc_Load_Force_WIFI =
            "检测到有新的游戏内容需要更新, 取消更新将导致无法进入游戏，您当前已为<color=#BA3026>wifi网络</color>，请开始更新";

        public string Label_Dlc_Load_Force_NO_WIFI =
            "检测到有新的游戏内容需要更新, 取消更新将导致无法进入游戏，请开始更新";

        public string Label_Had_Update = "检测到有版本更新...";
        public string Label_RequestVersionIng = "正在向服务器请求版本信息中...";
        public string Label_RequestVersionInfo = "正在向服务器请求版本信息{0}次";
    }

    public class LoadText : TextMode
    {
        private static LoadText _instance;

        public static LoadText Instance => _instance ??= new LoadText();

        public void InitConfigData(TextAsset asset)
        {
            if (asset == null)
                return;

            TextMode loadConfig = JsonUtility.FromJson<TextMode>(asset.text);
            if (loadConfig != null)
            {
                Label_Load_Progress = loadConfig.Label_Load_Progress;
                Label_Load_FirstUnpack = loadConfig.Label_Load_FirstUnpack;
                Label_Load_Unpacking = loadConfig.Label_Load_Unpacking;
                Label_Load_Checking = loadConfig.Label_Load_Checking;
                Label_Load_Checked = loadConfig.Label_Load_Checked;
                Label_Load_Package = loadConfig.Label_Load_Package;
                Label_Load_Plantform = loadConfig.Label_Load_Plantform;
                Label_Load_Notice = loadConfig.Label_Load_Notice;
                Label_Load_Force = loadConfig.Label_Load_Force;
                Label_Load_Force_WIFI = loadConfig.Label_Load_Force_WIFI;
                Label_Load_Force_NO_WIFI = loadConfig.Label_Load_Force_NO_WIFI;
                Label_Load_Error = loadConfig.Label_Load_Error;
                Label_Load_FirstEntrerGame_Error = loadConfig.Label_Load_FirstEntrerGame_Error;
                Label_Load_UnpackComplete = loadConfig.Label_Load_UnpackComplete;
                Label_Load_UnPackError = loadConfig.Label_Load_UnPackError;
                Label_Load_Load_Progress = loadConfig.Label_Load_Load_Progress;
                Label_Load_Download_Progress = loadConfig.Label_Load_Download_Progress;
                Label_Load_Load_Complete = loadConfig.Label_Load_Load_Complete;
                Label_Load_Init = loadConfig.Label_Load_Init;
                Label_Net_UnReachable = loadConfig.Label_Net_UnReachable;
                Label_Net_Error = loadConfig.Label_Net_Error;
                Label_Net_Changed = loadConfig.Label_Net_Changed;
                Label_Data_Empty = loadConfig.Label_Data_Empty;
                Label_Memory_Low = loadConfig.Label_Memory_Low;
                Label_Memory_Low_Load = loadConfig.Label_Memory_Low_Load;
                Label_Memory_UnZip_Low = loadConfig.Label_Memory_UnZip_Low;
                Label_App_id = loadConfig.Label_App_id;
                Label_Res_id = loadConfig.Label_Res_id;
                Label_Clear_Comfirm = loadConfig.Label_Clear_Comfirm;
                Label_RestartApp = loadConfig.Label_RestartApp;
                Label_DownLoadFailed = loadConfig.Label_DownLoadFailed;
                Label_ClearConfig = loadConfig.Label_ClearConfig;
                Label_PreventionOfAddiction = loadConfig.Label_PreventionOfAddiction;
                Label_RegionInfoIllegal = loadConfig.Label_RegionInfoIllegal;
                Label_RemoteUrlisNull = loadConfig.Label_RemoteUrlisNull;
                Label_FirstPackageNotFound = loadConfig.Label_FirstPackageNotFound;
                Label_RequestReginInfo = loadConfig.Label_RequestReginInfo;
                Label_Net_ReachableViaCarrierDataNetwork = loadConfig.Label_Net_ReachableViaCarrierDataNetwork;
                Label_RequestTimeOut = loadConfig.Label_RequestTimeOut;
                Label_Region_ArgumentError = loadConfig.Label_Region_ArgumentError;
                Label_Region_IndexOutOfRange = loadConfig.Label_Region_IndexOutOfRange;
                Label_Region_NonConfigApplication = loadConfig.Label_Region_NonConfigApplication;
                Label_Region_SystemError = loadConfig.Label_Region_SystemError;
                Label_Btn_Ignore = loadConfig.Label_Btn_Ignore;
                Label_Btn_Package = loadConfig.Label_Btn_Package;
                Label_Btn_Update = loadConfig.Label_Btn_Update;
            }
        }
    }
}