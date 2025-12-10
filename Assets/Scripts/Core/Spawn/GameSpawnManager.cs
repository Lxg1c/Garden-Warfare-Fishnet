using FishNet.Connection;
using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;
using Player.Components;

public class GameSpawnManager : NetworkBehaviour
{
    public static GameSpawnManager Instance;

    [Header("Prefabs (�� ����� Project!)")]
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

        Debug.Log("SPAWNER: ������ �������. ������������ �� �������...");
        
        NetworkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedScene;
        
        foreach (NetworkConnection conn in ServerManager.Clients.Values)
        {
            // ����������� �����: ��������� ������ (true)
            if (conn.LoadedStartScenes(true))
            {
                Debug.Log($"SPAWNER: ����� {conn.ClientId} ��� ���. ������� �������.");
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

        // ������ �� �������� ������: ���������, ���� �� � ������ ��� �������
        if (conn.Objects.Count > 0)
        {
            // ��� ������ ��������, �� ��� ������� ������, �� ������� �� �� ������
            // Debug.LogWarning($"� ������ {conn.ClientId} ��� ���� �������! ����������?");
            // return; // ����������������, ���� ����� ���������
        }

        Debug.Log($"SPAWNER: ������� ������� ��������� ��� ID {conn.ClientId}...");

        if (playerPrefab == null)
        {
            Debug.LogError("������: � GameSpawnManager �� �������� Player Prefab!");
            return;
        }

        Transform pSpawn = GetPlayerSpawnPoint(conn.ClientId);
        Transform fSpawn = GetFruitSpawnPoint(conn.ClientId);

        // --- ����� ������ ---
        NetworkObject playerObj = Instantiate(playerPrefab, pSpawn.position, pSpawn.rotation);
        ServerManager.Spawn(playerObj, conn);
        Debug.Log($"SPAWNER: ����� ������ �� ����������� {pSpawn.position}");

        // --- Настройка PlayerInfo ---
        PlayerInfo info = playerObj.GetComponent<PlayerInfo>();
        if (info != null)
        {
            info.SetActorNumber(conn.ClientId);
            info.SetSpawnPoint(pSpawn); // Сохраняем точку спавна для респавна
        }

        // --- ����� ������ ---
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
            Debug.LogWarning("��� ����� ������! ������� � (0, 1, 0)");
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