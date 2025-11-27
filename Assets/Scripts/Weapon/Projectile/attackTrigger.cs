using UnityEngine;
using Core.Interfaces;

namespace Core.Combat
{
    /// <summary>
    /// Универсальный триггер атаки.
    /// Работает как для нейтралов, так и для игроков, реализующих IAttackableZoneHandler.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class AttackTrigger : MonoBehaviour
    {
        private IAttackableZoneHandler _owner;

        private void Awake()
        {
            _owner = GetComponentInParent<IAttackableZoneHandler>();
            if (_owner == null)
            {
                Debug.LogError($"[{name}] AttackTrigger: родитель не реализует IAttackableZoneHandler!");
            }

            var collider = GetComponent<Collider>();
            if (collider != null)
                collider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_owner == null) return;

            if (other.CompareTag(_owner.TargetTag))
                _owner.OnEnterAttackRange(other.transform);
        }

        private void OnTriggerExit(Collider other)
        {
            if (_owner == null) return;

            if (other.CompareTag(_owner.TargetTag))
                _owner.OnExitAttackRange(other.transform);
        }
    }
}