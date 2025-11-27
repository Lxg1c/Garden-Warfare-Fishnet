using System.Collections;
using FishNet;
using FishNet.Discovery;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using System.Net;

public class LanConnectionManager : MonoBehaviour
{
    [Header("Настройки")]
    public string gameSceneName = "GameScene";
    public float searchTimeout = 1.0f;

    [Header("Ссылки на UI")]
    public GameObject mainButtonsPanel;
    public GameObject modeSelectionPanel;
    public GameObject loadingPanel;

    private NetworkManager _networkManager;
    private NetworkDiscovery _networkDiscovery;
    private Tugboat _tugboat;

    private bool _isConnecting;

    private void Start()
    {
        _networkManager = InstanceFinder.NetworkManager;

        if (_networkManager == null)
        {
            Debug.LogError("CRITICAL: NetworkManager не найден!");
            return;
        }

        _networkDiscovery = _networkManager.GetComponent<NetworkDiscovery>();
        _tugboat = _networkManager.GetComponent<Tugboat>();

        if (_networkDiscovery != null)
            _networkDiscovery.ServerFoundCallback += OnServerFound;

        ShowMainPanel();
    }

    private void OnDestroy()
    {
        if (_networkDiscovery != null)
            _networkDiscovery.ServerFoundCallback -= OnServerFound;

        // Важно: отписываемся от события сервера, чтобы не было утечек памяти
        if (_networkManager != null)
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
    }

    // --- UI ---
    public void OnPlayButtonClicked() { mainButtonsPanel.SetActive(false); modeSelectionPanel.SetActive(true); }
    public void OnBackButtonClicked() { ShowMainPanel(); }
    public void OnLanButtonClicked()
    {
        if (_isConnecting) return;
        StartCoroutine(StartLanLogic());
    }
    public void OnSteamButtonClicked() { Debug.Log("Steam in dev..."); }

    private void ShowMainPanel()
    {
        mainButtonsPanel.SetActive(true);
        modeSelectionPanel.SetActive(false);
        if (loadingPanel) loadingPanel.SetActive(false);
        _isConnecting = false;
    }

    // --- ЛОГИКА ---

    private IEnumerator StartLanLogic()
    {
        _isConnecting = true;
        if (loadingPanel) loadingPanel.SetActive(true);
        modeSelectionPanel.SetActive(false);

        Debug.Log("LAN: Поиск...");
        _networkDiscovery.SearchForServers();

        yield return new WaitForSeconds(searchTimeout);

        // Если не нашли сервер -> Создаем свой
        CreateHost();
    }

    private void OnServerFound(IPEndPoint endPoint)
    {
        if (!_isConnecting) return;
        Debug.Log($"LAN: Нашли сервер {endPoint.Address}");
        StopAllCoroutines();
        _networkDiscovery.StopSearchingOrAdvertising();
        _tugboat.SetClientAddress(endPoint.Address.ToString());
        _networkManager.ClientManager.StartConnection();
    }

    // --- ИСПРАВЛЕННАЯ ЛОГИКА СОЗДАНИЯ ХОСТА ---

    private void CreateHost()
    {
        Debug.Log("LAN: Создаем хост...");
        _networkDiscovery.StopSearchingOrAdvertising();

        // 1. Подписываемся на событие "Состояние сервера изменилось"
        // Мы хотим загрузить сцену ТОЛЬКО когда сервер скажет "Я ЗАПУЩЕН"
        _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;

        // 2. Запускаем сервер и клиент
        _networkManager.ServerManager.StartConnection();
        _networkManager.ClientManager.StartConnection();

        // 3. Запускаем рекламу (чтобы другие видели нас в поиске)
        _networkDiscovery.AdvertiseServer();
    }

    // Этот метод сработает сам, как только сервер реально запустится
    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        // Проверяем, что сервер именно ЗАПУСТИЛСЯ (Started)
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            Debug.Log("СЕРВЕР ЗАПУЩЕН! Начинаем загрузку сцены...");

            // Сразу отписываемся, чтобы не загружать сцену повторно при перезапусках
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;

            // Формируем данные для загрузки
            SceneLoadData sld = new SceneLoadData(gameSceneName);
            sld.ReplaceScenes = ReplaceOption.All;

            // Загружаем
            _networkManager.SceneManager.LoadGlobalScenes(sld);
        }
    }
}