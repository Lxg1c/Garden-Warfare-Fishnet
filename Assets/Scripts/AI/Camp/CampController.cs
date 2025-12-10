using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace AI.Camp
{
    public class CampController : NetworkBehaviour
    {
        private List<Neutral.Neutral> _units = new();
        private Camp _camp;
        
        // Инициализация вызывается из Camp после спавна
        [Server]
        public void InitializeUnits(Camp camp)
        {
            _camp = camp;
            
            // Находим всех нейтралов в дочерних объектах
            _units.Clear();
            var found = GetComponentsInChildren<Neutral.Neutral>();
            
            foreach (var neutral in found)
            {
                _units.Add(neutral);
                
                // Регистрируем юнит в Camp
                if (IsServerInitialized && _camp != null)
                {
                    _camp.RegisterUnit(neutral);
                }
                
                // Подписываемся на событие получения урона
                neutral.OnNeutralGetDamage -= OnNeutralDamaged;
                neutral.OnNeutralGetDamage += OnNeutralDamaged;
            }
            
            Debug.Log($"[CampController] Initialized with {_units.Count} units");
        }

        private void OnDestroy()
        {
            // Отписываемся от всех юнитов
            foreach (var unit in _units)
            {
                if (unit != null)
                {
                    unit.OnNeutralGetDamage -= OnNeutralDamaged;
                }
            }
        }

        // Вызывается когда любой нейтрал получает урон
        private void OnNeutralDamaged(Transform attacker)
        {
            // Событие вызывается через ObserversRpc, поэтому нужна проверка
            if (!IsServerInitialized) return;
            if (attacker == null) return;

            Debug.Log($"[CampController] Neutral damaged by {attacker.name}, propagating aggro...");

            // Агрим весь лагерь на атакующего
            PropagateAggroToAll(attacker);
        }

        // Распространяем агро на всех живых юнитов
        [Server]
        private void PropagateAggroToAll(Transform attacker)
        {
            int aggroCount = 0;
            
            foreach (var neutral in _units)
            {
                if (neutral != null && neutral.gameObject.activeSelf && neutral.enabled)
                {
                    neutral.SetAggro(attacker);
                    aggroCount++;
                }
            }
            
            Debug.Log($"[CampController] Propagated aggro to {aggroCount}/{_units.Count} units on {attacker.name}");
        }

        // Вызывается из Camp когда юнит умирает
        public void OnUnitRemoved(Neutral.Neutral unit)
        {
            if (_units.Remove(unit))
            {
                unit.OnNeutralGetDamage -= OnNeutralDamaged;
                Debug.Log($"[CampController] Unit removed from controller. Remaining: {_units.Count}");
            }
        }

        // Опциональный метод для внешней агрессии (например, от игрока)
        [Server]
        public void SetAggroOnAll(Transform target)
        {
            PropagateAggroToAll(target);
        }
    }
}