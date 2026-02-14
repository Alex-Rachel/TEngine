
namespace YooAsset
{
    internal class ClearCacheFilesOptions
    {
        /// <summary>
        /// 清理模式
        /// </summary>
        public string ClearMode;

        /// <summary>
        /// 附加参数
        /// </summary>
        public object ClearParam;
    }

    internal abstract class FSClearCacheFilesOperation : AsyncOperationBase
    {
    }

    internal sealed class FSClearCacheFilesCompleteOperation : FSClearCacheFilesOperation
    {
        private readonly string _error;

        internal FSClearCacheFilesCompleteOperation()
        {
            _error = null;
        }
        internal FSClearCacheFilesCompleteOperation(string error)
        {
            _error = error;
        }
        internal override void InternalStart()
        {
            if (string.IsNullOrEmpty(_error))
            {
                Status = EOperationStatus.Succeed;
            }
            else
            {
                Status = EOperationStatus.Failed;
                Error = _error;
            }
        }
        internal override void InternalUpdate()
        {
        }
    }
}