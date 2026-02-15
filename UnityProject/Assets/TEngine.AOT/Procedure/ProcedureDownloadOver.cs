using Launcher;
using TEngine;
using UnityEngine;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    public class ProcedureDownloadOver : ProcedureBase
    {
        public override bool UseNativeDialog { get; }

        private bool _needClearCache;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            Log.Info("下载完成!!!");

            LauncherMgr.ShowUI<LoadUpdateUI>($"下载完成...");

            // 下载完成之后再保存本地版本。
            Utility.PlayerPrefs.SetString("GAME_VERSION", _resourceModule.PackageVersion);
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            if (_needClearCache)
            {
                ChangeState<ProcedureClearCache>(procedureOwner);
            }
            else
            {
                ChangeState<ProcedurePreload>(procedureOwner);
            }
        }
    }
}