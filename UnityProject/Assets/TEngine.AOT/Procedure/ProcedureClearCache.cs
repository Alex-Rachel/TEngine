using Launcher;
using TEngine;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    /// <summary>
    /// 流程 => 清理缓存。
    /// </summary>
    public class ProcedureClearCache : ProcedureBase
    {
        public override bool UseNativeDialog { get; }

        private ProcedureOwner _procedureOwner;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            _procedureOwner = procedureOwner;
            Log.Info("清理未使用的缓存文件！");

            LauncherMgr.ShowUI<LoadUpdateUI>($"清理未使用的缓存文件...");

            var operation = _resourceModule.ClearCacheFilesAsync();
            operation.Completed += Operation_Completed;
        }


        private void Operation_Completed(YooAsset.AsyncOperationBase obj)
        {
            LauncherMgr.ShowUI<LoadUpdateUI>($"清理完成 即将进入游戏...");

            ChangeState<ProcedurePreload>(_procedureOwner);
        }
    }
}