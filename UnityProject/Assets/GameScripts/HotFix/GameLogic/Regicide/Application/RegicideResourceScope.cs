using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic.Regicide
{
    /// <summary>
    /// Tracks regicide dynamic assets to guarantee load/unload pairing.
    /// </summary>
    public sealed class RegicideResourceScope : Singleton<RegicideResourceScope>
    {
        public static readonly string[] BattleAssetChecklist =
        {
            "Regicide/CardAtlas",
            "Regicide/EnemyAtlas",
            "Regicide/BattleVfx",
            "Regicide/BattleSfx",
        };

        private readonly Dictionary<string, Object> _loadedAssets = new Dictionary<string, Object>();
        private readonly List<GameObject> _spawnedObjects = new List<GameObject>();

        public async UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default) where T : Object
        {
            if (!GameModule.Resource.CheckLocationValid(location))
            {
                return null;
            }

            T asset = await GameModule.Resource.LoadAssetAsync<T>(location, cancellationToken);
            if (asset != null)
            {
                _loadedAssets[location] = asset;
            }

            return asset;
        }

        public async UniTask<GameObject> LoadBattleObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            if (!GameModule.Resource.CheckLocationValid(location))
            {
                return null;
            }

            GameObject go = await GameModule.Resource.LoadGameObjectAsync(location, parent, cancellationToken);
            if (go != null)
            {
                _spawnedObjects.Add(go);
            }

            return go;
        }

        public void ReleaseBattleAssets()
        {
            for (int i = _spawnedObjects.Count - 1; i >= 0; i--)
            {
                GameObject go = _spawnedObjects[i];
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _spawnedObjects.Clear();

            foreach (KeyValuePair<string, Object> pair in _loadedAssets)
            {
                if (pair.Value != null)
                {
                    GameModule.Resource.UnloadAsset(pair.Value);
                }
            }

            _loadedAssets.Clear();
        }
    }
}
