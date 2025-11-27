using UnityEngine;
// using Weapon.Interfaces; // IReloadable больше не нужен

namespace Weapon.Base
{
    /// <summary>
    /// Базовый класс для дальнобойного оружия.
    /// Теперь просто наследует логику таймера, патроны вырезаны.
    /// </summary>
    public abstract class RangedWeapon : WeaponBase
    {
        public override bool CanUse()
        {
            // Просто возвращаем проверку таймера из базы
            return base.CanUse();
        }

        public override void Use()
        {
            // Просто обновляем таймер стрельбы
            MarkUse();
        }
    }
}