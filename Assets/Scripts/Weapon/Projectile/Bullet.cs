using Core.Components;
using UnityEngine;
using FishNet.Object;

namespace Weapon.Projectile
{
    public enum BulletOwnerType
    {
        Player,
        Turret,
        Bot
    }

    [RequireComponent(typeof(Rigidbody))]
    public class Bullet : NetworkBehaviour
    {
        [Header("Bullet Settings")]
        [SerializeField] private int damage = 10;
        [SerializeField] private float lifetime = 3f;
        [SerializeField] private GameObject impactEffect;

        private Transform _owner;
        private NetworkObject _ownerNetworkObject;
        private int _ownerId = -1;
        private BulletOwnerType _ownerType = BulletOwnerType.Player;

        /// <summary>
        /// Устанавливает владельца пули (игрок)
        /// </summary>
        public void SetOwner(Transform owner)
        {
            _owner = owner;
            _ownerNetworkObject = owner != null ? owner.GetComponent<NetworkObject>() : null;
            _ownerId = _ownerNetworkObject != null ? _ownerNetworkObject.OwnerId : -1;
            _ownerType = BulletOwnerType.Player;
        }

        /// <summary>
        /// Устанавливает владельца пули (турель)
        /// </summary>
        public void SetOwner(Transform turret, int ownerId)
        {
            _owner = turret;
            _ownerNetworkObject = turret != null ? turret.GetComponent<NetworkObject>() : null;
            _ownerId = ownerId;
            _ownerType = BulletOwnerType.Turret;
        }

        /// <summary>
        /// Устанавливает владельца пули (бот)
        /// </summary>
        public void SetBotOwner(Transform bot, int botId)
        {
            _owner = bot;
            _ownerNetworkObject = bot != null ? bot.GetComponent<NetworkObject>() : null;
            _ownerId = botId;
            _ownerType = BulletOwnerType.Bot;
        }

        public void SetDamage(float newDamage)
        {
            damage = Mathf.RoundToInt(newDamage);
        }

        public void SetDamage(int newDamage)
        {
            damage = newDamage;
        }

        public Transform GetOwnerTransform() => _owner;
        public int GetOwnerId() => _ownerId;
        public BulletOwnerType GetOwnerType() => _ownerType;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            if (IsServerInitialized)
            {
                Invoke(nameof(DespawnBullet), lifetime);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServerInitialized) return;

            // Не бьём владельца
            if (_owner != null && other.transform == _owner) return;

            // Проверяем NetworkObject на объекте или родителе
            var netObj = other.GetComponent<NetworkObject>();
            if (netObj == null) netObj = other.GetComponentInParent<NetworkObject>();

            // Не бьём владельца по OwnerId (только для игроков с валидным ID)
            if (_ownerType == BulletOwnerType.Player && _ownerId >= 0)
            {
                if (netObj != null && netObj.OwnerId == _ownerId) return;
            }

            // Специфичные проверки для турели
            if (_ownerType == BulletOwnerType.Turret)
            {
                // Турель не бьёт нейтралов
                if (other.GetComponent<AI.Neutral.Neutral>() != null) return;
                if (other.GetComponentInParent<AI.Neutral.Neutral>() != null) return;

                // Турель не бьёт другие турели того же владельца
                var turret = other.GetComponent<Gameplay.TurretPlant.TurretPlant>();
                if (turret == null) turret = other.GetComponentInParent<Gameplay.TurretPlant.TurretPlant>();
                if (turret != null && turret.PlantedOwnerId == _ownerId) return;
            }

            // Специфичные проверки для бота - бот не бьёт себя и свои турели
            if (_ownerType == BulletOwnerType.Bot)
            {
                // Не бьём свои турели
                var turret = other.GetComponent<Gameplay.TurretPlant.TurretPlant>();
                if (turret == null) turret = other.GetComponentInParent<Gameplay.TurretPlant.TurretPlant>();
                if (turret != null && turret.PlantedOwnerId == _ownerId) return;
            }

            // Ищем Health на самом объекте ИЛИ на родителях
            Health hp = other.GetComponent<Health>();
            if (hp == null)
            {
                hp = other.GetComponentInParent<Health>();
            }

            if (hp != null)
            {
                hp.TakeDamage(damage, _owner, _ownerNetworkObject);
                Debug.Log($"[Bullet] Hit {hp.name} for {damage} damage (owner type: {_ownerType})");
            }
            else
            {
                SpawnImpactEffect();
            }

            DespawnBullet();
        }

        [Server]
        private void SpawnImpactEffect()
        {
            if (impactEffect != null)
            {
                GameObject effect = Instantiate(impactEffect, transform.position, transform.rotation);
                NetworkObject netObj = effect.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    Spawn(netObj);
                }
                Destroy(effect, 2f);
            }
        }

        [Server]
        private void DespawnBullet()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                SpawnImpactEffect();
                NetworkObject.Despawn();
            }
        }
    }
}
