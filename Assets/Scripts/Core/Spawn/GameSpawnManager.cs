using FishNet;
using FishNet.Connection;
using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;
using Player.Components;

public class GameSpawnManager : NetworkBehaviour
{
    public static GameSpawnManager Instance;

    [Header("Prefabs (Из папки Project!)")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private NetworkObject lifeFruitPrefab;

    [Header("Spawn Points")]
    [SerializeField] private List<Transform> playerSpawnPoints;
    [SerializeField] private List<Transform> lifeFruitSpawnPoints;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        Debug.Log("SPAWNER: Сервер запущен. Подписываюсь на события...");

        // 1. Подписываемся на будущие подключения
        NetworkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedScene;

        // 2. ВАЖНО: Проверяем тех, кто УЖЕ загрузился (например, Хоста)
        foreach (NetworkConnection conn in ServerManager.Clients.Values)
        {
            // ИСПРАВЛЕНИЕ ЗДЕСЬ: Добавлены скобки (true)
            if (conn.LoadedStartScenes(true))
            {
                Debug.Log($"SPAWNER: Игрок {conn.ClientId} уже тут. Спауним вручную.");
                OnClientLoadedScene(conn, true);
            }
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (NetworkManager != null && NetworkManager.SceneManager != null)
            NetworkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedScene;
    }

    private void OnClientLoadedScene(NetworkConnection conn, bool asServer)
    {
        if (!asServer) return;

        // Защита от двойного спауна: проверяем, есть ли у игрока уже объекты
        if (conn.Objects.Count > 0)
        {
            // Это грубая проверка, но она поможет понять, не спауним ли мы дважды
            // Debug.LogWarning($"У игрока {conn.ClientId} уже есть объекты! Пропускаем?");
            // return; // Раскомментируйте, если будут дубликаты
        }

        Debug.Log($"SPAWNER: Попытка создать персонажа для ID {conn.ClientId}...");

        if (playerPrefab == null)
        {
            Debug.LogError("ОШИБКА: В GameSpawnManager не назначен Player Prefab!");
            return;
        }

        Transform pSpawn = GetPlayerSpawnPoint(conn.ClientId);
        Transform fSpawn = GetFruitSpawnPoint(conn.ClientId);

        // --- СПАУН ИГРОКА ---
        NetworkObject playerObj = Instantiate(playerPrefab, pSpawn.position, pSpawn.rotation);
        ServerManager.Spawn(playerObj, conn);
        Debug.Log($"SPAWNER: Игрок создан на координатах {pSpawn.position}");

        // --- НАСТРОЙКА PlayerInfo ---
        PlayerInfo info = playerObj.GetComponent<PlayerInfo>();
        if (info != null)
        {
            info.SetActorNumber(conn.ClientId);
        }

        // --- СПАУН ФРУКТА ---
        if (lifeFruitPrefab != null)
        {
            NetworkObject fruitObj = Instantiate(lifeFruitPrefab, fSpawn.position, fSpawn.rotation);
            ServerManager.Spawn(fruitObj, conn);
        }
    }

    public Transform GetPlayerSpawnPoint(int id)
    {
        if (playerSpawnPoints == null || playerSpawnPoints.Count == 0)
        {
            Debug.LogWarning("Нет точек спауна! Спауним в (0, 1, 0)");
            return transform;
        }
        return playerSpawnPoints[id % playerSpawnPoints.Count];
    }

    private Transform GetFruitSpawnPoint(int id)
    {
        if (lifeFruitSpawnPoints == null || lifeFruitSpawnPoints.Count == 0) return transform;
        return lifeFruitSpawnPoints[id % lifeFruitSpawnPoints.Count];
    }
}