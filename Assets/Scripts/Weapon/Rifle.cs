using UnityEngine;
using Weapon.Types;

namespace Weapon
{
    public class Rifle : RangedWeapon
    {
        public GameObject bulletPrefab;
        public Transform shootPoint;
        public float bulletForce = 1f;

        public override void UseLocal()
        {
            if (!MarkRangedUse()) return;

            Debug.Log("Rifle fired locally!");
        }

        public override void ReloadLocal()
        {
            Debug.Log($"Reloading {weaponName}");
        }

        public override void Reload()
        {
            currentAmmo = maxAmmo;
        }

        public override bool CanReload()
        {
            return currentAmmo < maxAmmo;
        }
    }
}