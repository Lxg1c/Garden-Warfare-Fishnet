using FischlWorks_FogWar;
using UnityEngine;

namespace Core.Spawn
{
    public class FogOfWarController : MonoBehaviour
    {
        [SerializeField] private csFogWar fogPrefab;
        private csFogWar _fogInstance;
        private GameObject _levelMidPoint;

        // Публичное свойство для доступа к экземпляру тумана
        public csFogWar FogInstance => _fogInstance;

        /// <summary>
        /// Создаёт локальный туман для игрока.
        /// </summary>
        public void InitializeForPlayer(Transform player, int radius = 10)
        {
            if (_fogInstance != null)
                return;

            if (fogPrefab == null)
            {
                Debug.LogError("FogOfWar prefab not assigned!");
                return;
            }
            _fogInstance = Instantiate(fogPrefab);
            _fogInstance.name = "Local Fog Of War";
            _levelMidPoint = GameObject.Find("Floor");
            _fogInstance.SetLevelMidPoint(_levelMidPoint.transform);
            
            _fogInstance._FogRevealers.Add(
                new csFogWar.FogRevealer(player, radius, true)
            );

            Debug.Log("Local Fog Of War created for player");
        }

        /// <summary>
        /// Возвращает экземпляр тумана войны
        /// </summary>
        public csFogWar GetFogInstance()
        {
            return _fogInstance;
        }
    }
}