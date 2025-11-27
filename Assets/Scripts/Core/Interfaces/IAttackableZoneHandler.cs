using UnityEngine;

namespace Core.Interfaces
{
    /// <summary>
    /// Интерфейс для объектов, реагирующих на вход/выход целей из зоны атаки.
    /// Используется AI нейтралов и игроков.
    /// </summary>
    public interface IAttackableZoneHandler
    {
        void OnEnterAttackRange(Transform target);
        void OnExitAttackRange(Transform target);
        string TargetTag { get; }
    }
}