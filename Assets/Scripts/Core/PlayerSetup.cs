using FishNet.Object;
using UnityEngine;
using Unity.Cinemachine; // Если Unity 6 / New Cinemachine
// using Cinemachine; // Если старый Cinemachine
using FischlWorks_FogWar;

public class PlayerSetup : NetworkBehaviour
{
    [Header("Ссылки (можно перетащить в инспекторе или искать кодом)")]
    [SerializeField] private CinemachineCamera cameraPrefab; // Если нужно создавать камеру
    // Или если камера уже на сцене:
    // [SerializeField] private CinemachineCamera sceneCamera; 

    public override void OnStartClient()
    {
        base.OnStartClient();

        // IsOwner аналог photonView.IsMine
        if (!IsOwner)
        {
            // Тут можно отключить управление для чужих игроков
            return;
        }

        // --- Все, что ниже, выполняется ТОЛЬКО для МОЕГО игрока ---

        Debug.Log("CLIENT: Это мой игрок, настраиваю камеру и туман.");

        // 1. Настройка Камеры
        // Вариант А: Создаем камеру из префаба (как было у тебя)
        if (cameraPrefab != null)
        {
            var cam = Instantiate(cameraPrefab);
            cam.Follow = transform;
            cam.LookAt = transform;
        }
        // Вариант Б: Ищем камеру на сцене (чаще используется)
        else
        {
            var cam = FindFirstObjectByType<CinemachineCamera>();
            if (cam != null)
            {
                cam.Follow = transform;
                cam.LookAt = transform;
            }
        }

        // 2. Настройка Тумана (Fog of War)
        var fogWar = FindFirstObjectByType<csFogWar>();
        if (fogWar != null)
        {
            fogWar._FogRevealers.Add(
                new csFogWar.FogRevealer(transform, 10, true)
            );
        }
    }
}