using System.Collections;
using FishNet;
using FishNet.Discovery;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using System.Net;

namespace Gameplay
{
    public class LanConnectionManager : MonoBehaviour
    {
        public string gameSceneName = "Game";
        public float searchTimeout = 1.0f;

        private NetworkManager _networkManager;
        private NetworkDiscovery _networkDiscovery;
        private Tugboat _tugboat;

        private bool _isConnecting;

        public GameObject settingsCanvas;

        private void Start()
        {
            _networkManager = InstanceFinder.NetworkManager;

            if (_networkManager == null)
            {
                Debug.LogError("CRITICAL: NetworkManager!");
                return;
            }

            _networkDiscovery = _networkManager.GetComponent<NetworkDiscovery>();
            _tugboat = _networkManager.GetComponent<Tugboat>();

            if (_networkDiscovery != null)
                _networkDiscovery.ServerFoundCallback += OnServerFound;
        }

        public void OnSettingsOpen()
        {
            if (settingsCanvas == null)
            {
                Debug.LogError("Settings Canvas is null! Make sure it's assigned in the inspector.");
                return;
            }

            // Переключаем состояние: если активно - деактивируем, если неактивно - активируем
            bool newState = !settingsCanvas.activeSelf;
            settingsCanvas.SetActive(newState);
            Debug.Log($"Settings Canvas {(newState ? "opened" : "closed")}");
        }

        private void OnDestroy()
        {
            if (_networkDiscovery != null)
                _networkDiscovery.ServerFoundCallback -= OnServerFound;
            
            if (_networkManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }

        public void OnLanButtonClicked()
        {
            if (_isConnecting) return;
            StartCoroutine(StartLanLogic());
        }

        public void OnSteamButtonClicked()
        {
            Debug.Log("Steam in dev...");
        }

        private IEnumerator StartLanLogic()
        {
            _isConnecting = true;

            _networkDiscovery.SearchForServers();

            yield return new WaitForSeconds(searchTimeout);

            CreateHost();
        }

        private void OnServerFound(IPEndPoint endPoint)
        {
            if (!_isConnecting) return;

            StopAllCoroutines();
            _networkDiscovery.StopSearchingOrAdvertising();
            _tugboat.SetClientAddress(endPoint.Address.ToString());
            _networkManager.ClientManager.StartConnection();
        }


        private void CreateHost()
        {
            _networkDiscovery.StopSearchingOrAdvertising();

            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;

            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection();

            _networkDiscovery.AdvertiseServer();
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {

                _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;


                SceneLoadData sld = new SceneLoadData(gameSceneName);
                sld.ReplaceScenes = ReplaceOption.All;

                _networkManager.SceneManager.LoadGlobalScenes(sld);
            }
        }
    }
}
