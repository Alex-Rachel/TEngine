using System.Collections.Generic;

namespace YooAsset
{
    internal class DefaultUnpackRemoteServices : IRemoteServices
    {
        private readonly string _buildinPackageRoot;
        protected readonly Dictionary<string, string> _mapping = new Dictionary<string, string>(10000);

        public DefaultUnpackRemoteServices(string buildinPackRoot)
        {
            _buildinPackageRoot = buildinPackRoot;
        }
        string IRemoteServices.GetRemoteMainURL(string fileName)
        {
            return GetFileLoadURL(fileName);
        }
        string IRemoteServices.GetRemoteFallbackURL(string fileName)
        {
            return GetFileLoadURL(fileName);
        }

        private string GetFileLoadURL(string fileName)
        {
            if (_mapping.TryGetValue(fileName, out string url) == false)
            {
                string filePath = PathUtility.Combine(_buildinPackageRoot, fileName);
                url = DownloadSystemHelper.ConvertToWWWPath(filePath);
                _mapping.Add(fileName, url);
            }
            return url;
        }
    }
}