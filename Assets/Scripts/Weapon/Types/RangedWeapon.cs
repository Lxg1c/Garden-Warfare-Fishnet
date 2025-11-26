using UnityEngine;
using Weapon.Base;
using Weapon.Interfaces;

namespace Weapon.Types
{
    public abstract class RangedWeapon : WeaponBase, IReloadable
    {
        [Header("Ranged Weapon Settings")]
        [SerializeField] protected int maxAmmo = 30;
        [SerializeField] protected int currentAmmo = 30;

        private float _lastFireTime;

        public override bool CanUse()
        {
            return currentAmmo > 0 && 
                   Time.time >= _lastFireTime + useRate;
        }

        protected bool MarkRangedUse()
        {
            if (!CanUse()) return false;

            currentAmmo--;
            _lastFireTime = Time.time;

            return true;
        }

        public abstract void Reload();
        public abstract bool CanReload();

        public abstract void ReloadLocal();
    }
}