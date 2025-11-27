using FischlWorks_FogWar;
using FishNet.Object;
using UnityEngine;

namespace Core.Spawn
{
    public class FogOfWar : NetworkBehaviour
    {
        [SerializeField] private csFogWar fogOfWarPrefab;
        private csFogWar _spawnedFogInstance;

        #region Initialization

        [ServerRpc]
        public void InitializeFogOfWarServerRpc()
        {
            if (fogOfWarPrefab == null)
            {
                Debug.LogError("FogOfWar prefab is not assigned!");
                return;
            }

            if (!IsServerInitialized)
            {
                Debug.LogWarning("Only server can initialize fog of war!");
                return;
            }

            _spawnedFogInstance = Instantiate(fogOfWarPrefab, Vector3.zero, Quaternion.identity);

            NetworkObject networkObject = _spawnedFogInstance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                networkObject = _spawnedFogInstance.gameObject.AddComponent<NetworkObject>();
            }

            ServerManager.Spawn(networkObject);

            Debug.Log("Fog of war initialized and spawned on all clients");
        }

        #endregion

        #region Revealer Management

        [ServerRpc]
        public void AddFogRevealerServerRpc(NetworkObject revealerNetworkObject, int radius = 10)
        {
            if (_spawnedFogInstance == null)
            {
                Debug.LogWarning("Fog of war not initialized!");
                return;
            }

            if (revealerNetworkObject == null)
            {
                Debug.LogWarning("Revealer NetworkObject is null!");
                return;
            }
            
            AddRevealerInternal(revealerNetworkObject.transform, radius);
            
            AddFogRevealerClientRpc(revealerNetworkObject, radius);
        }

        [ServerRpc]
        public void RemoveFogRevealerServerRpc(NetworkObject revealerNetworkObject)
        {
            if (_spawnedFogInstance == null || revealerNetworkObject == null)
                return;
            
            RemoveRevealerInternal(revealerNetworkObject.transform);
            
            RemoveFogRevealerClientRpc(revealerNetworkObject);
        }

        [ObserversRpc]
        private void AddFogRevealerClientRpc(NetworkObject revealerNetworkObject, int radius)
        {
            if (revealerNetworkObject != null)
            {
                AddRevealerInternal(revealerNetworkObject.transform, radius);
            }
        }

        [ObserversRpc]
        private void RemoveFogRevealerClientRpc(NetworkObject revealerNetworkObject)
        {
            if (revealerNetworkObject != null)
            {
                RemoveRevealerInternal(revealerNetworkObject.transform);
            }
        }

        private void AddRevealerInternal(Transform revealerTransform, int radius)
        {
            if (_spawnedFogInstance != null && revealerTransform != null)
            {
                if (!HasRevealer(revealerTransform))
                {
                    _spawnedFogInstance._FogRevealers.Add(new csFogWar.FogRevealer(revealerTransform, radius, true));
                    Debug.Log($"Added fog revealer: {revealerTransform.name} with radius {radius}");
                }
            }
        }

        private void RemoveRevealerInternal(Transform revealerTransform)
        {
            if (_spawnedFogInstance != null && revealerTransform != null)
            {
                _spawnedFogInstance._FogRevealers.RemoveAll(r => r._RevealerTransform == revealerTransform);
                Debug.Log($"Removed fog revealer: {revealerTransform.name}");
            }
        }

        private bool HasRevealer(Transform revealerTransform)
        {
            return _spawnedFogInstance != null && 
                   _spawnedFogInstance._FogRevealers.Exists(r => r._RevealerTransform == revealerTransform);
        }

        #endregion

        #region Utility Methods

        public bool IsFogInitialized()
        {
            return _spawnedFogInstance != null;
        }

        public void ClearAllRevealers()
        {
            if (_spawnedFogInstance != null)
            {
                _spawnedFogInstance._FogRevealers.Clear();
                Debug.Log("Cleared all fog revealers");
            }
        }

        #endregion
    }
}