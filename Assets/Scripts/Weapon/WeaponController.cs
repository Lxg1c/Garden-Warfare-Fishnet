using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using Player.Components;
using Weapon.Base;
using Weapon.Types;

namespace Weapon
{
    public class WeaponController : NetworkBehaviour
    {
        [Header("References")]
        public WeaponBase currentWeapon;
        public CarryPlantAgent carry;

        [Header("Settings")]
        public bool holdToFire = true;

        private PlayerInputActions _input;
        private bool _isEnabled = true;

        private void Awake()
        {
            _input = new PlayerInputActions();
            if (carry == null) carry = GetComponent<CarryPlantAgent>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (base.IsOwner) _input.Enable();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (base.IsOwner) _input.Disable();
        }

        private void Update()
        {
            if (!base.IsOwner) return;
            if (!_isEnabled) return;

            // Если несем растение — не стреляем
            if (carry != null && carry.IsCarrying) return;
            if (currentWeapon == null) return;

            HandleShooting();
            // HandleReload() удален, так как патронов нет
        }

        private void HandleShooting()
        {
            bool trigger = holdToFire
                ? _input.Player.Fire.IsPressed()
                : _input.Player.Fire.WasPressedThisFrame();

            if (trigger && currentWeapon.CanUse())
            {
                UseWeaponServerRpc();
            }
        }

        /* =========================
         *     SERVER RPCs
         * ========================= */

        [ServerRpc]
        private void UseWeaponServerRpc()
        {
            if (currentWeapon == null || !currentWeapon.CanUse())
                return;

            // Обновляем таймер стрельбы
            currentWeapon.Use();

            // Спавним пулю
            SpawnBullet();

            // Визуал
            UseWeaponObserversRpc();
        }

        // ReloadServerRpc удален

        /* =========================
         *     OBSERVERS RPCs
         * ========================= */

        [ObserversRpc]
        private void UseWeaponObserversRpc()
        {
            if (currentWeapon != null)
                currentWeapon.UseLocal();
        }

        // ReloadObserversRpc удален

        /* =========================
         *     LOGIC
         * ========================= */

        private void SpawnBullet()
        {
            if (currentWeapon is Rifle rifle)
            {
                if (rifle.bulletPrefab == null || rifle.shootPoint == null) return;

                GameObject bullet = Instantiate(
                    rifle.bulletPrefab,
                    rifle.shootPoint.position,
                    rifle.shootPoint.rotation
                );

                Spawn(bullet);

                var bulletScript = bullet.GetComponent<Weapon.Projectile.Bullet>();
                if (bulletScript != null)
                {
                    bulletScript.SetOwner(this.Owner);
                    bulletScript.SetDamage(rifle.damage);
                }

                if (bullet.TryGetComponent<Rigidbody>(out var rb))
                {
                    rb.AddForce(rifle.shootPoint.forward * rifle.bulletForce, ForceMode.Impulse);
                }
            }
        }

        public void SetWeaponEnabled(bool isEnabled)
        {
            _isEnabled = isEnabled;
        }
    }
}