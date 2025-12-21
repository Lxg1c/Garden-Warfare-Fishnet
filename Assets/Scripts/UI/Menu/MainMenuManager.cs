using System.Collections;
using FishNet;
using FishNet.Discovery;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using System.Net;

namespace UI.Menu
{
    public class MainMenuManager : MonoBehaviour
    {
        public string gameSceneName = "Game";
        public float searchTimeout = 3.0f;

        private NetworkManager _networkManager;
        private NetworkDiscovery _networkDiscovery;
        private Tugboat _tugboat;

        private bool _isConnecting;
        private bool _serverFound;

        public GameObject settingsCanvas;
        public GameObject mainMenuCanvas;
        public GameObject howToPlayCanvas;

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
            if (settingsCanvas == null || mainMenuCanvas == null)
            {
                Debug.LogError("Settings Canvas or Main Menu Canvas is null! Make sure it's assigned in the inspector.");
                return;
            }

            settingsCanvas.SetActive(!settingsCanvas.activeSelf);
            mainMenuCanvas.SetActive(!mainMenuCanvas.activeSelf);
        }

        public void OnHowToPlayToggle()
        {
            howToPlayCanvas.SetActive(!howToPlayCanvas.activeSelf);
            mainMenuCanvas.SetActive(!mainMenuCanvas.activeSelf);
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
        
        private IEnumerator StartLanLogic()
        {
            _isConnecting = true;
            _serverFound = false;

            Debug.Log("[MainMenu] Searching for LAN servers...");
            _networkDiscovery.SearchForServers();

            yield return new WaitForSeconds(searchTimeout);

            // Создаём хост только если сервер не был найден
            if (!_serverFound)
            {
                Debug.Log("[MainMenu] No server found, creating host...");
                CreateHost();
            }
        }

        private void OnServerFound(IPEndPoint endPoint)
        {
            if (!_isConnecting || _serverFound) return;

            _serverFound = true;
            Debug.Log($"[MainMenu] Server found at {endPoint.Address}, connecting...");

            StopAllCoroutines();
            _networkDiscovery.StopSearchingOrAdvertising();
            _tugboat.SetClientAddress(endPoint.Address.ToString());
            _networkManager.ClientManager.StartConnection();

            _isConnecting = false;
        }


        private void CreateHost()
        {
            // Проверяем что клиент ещё не подключен (на случай если сервер нашёлся в последний момент)
            if (_networkManager.ClientManager.Started)
            {
                Debug.Log("[MainMenu] Client already connected, skipping host creation");
                return;
            }

            _networkDiscovery.StopSearchingOrAdvertising();

            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;

            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection();

            _networkDiscovery.AdvertiseServer();

            Debug.Log("[MainMenu] Host created and advertising");
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
        
        public void OnQuitGame()
        {
            #if UNITY_STANDALONE
                        Application.Quit();
            #endif
                            
            #if UNITY_EDITOR
                        UnityEditor.EditorApplication.isPlaying = false;
            #endif
        }
    }
}
