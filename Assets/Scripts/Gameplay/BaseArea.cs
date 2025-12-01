using FishNet.Object;
using UnityEngine;

namespace Gameplay
{
    // Этот скрипт должен висеть на объекте LifeFruit или дочернем объекте "Zone"
    public class BaseArea : MonoBehaviour
    {
        [SerializeField] private float radius = 6f;

        // Ссылка на NetworkObject родителя (LifeFruit)
        private NetworkObject _parentNO;

        private void Awake()
        {
            _parentNO = GetComponentInParent<NetworkObject>();
        }

        public float Radius => radius;

        // Получаем ID владельца базы
        public int OwnerId
        {
            get
            {
                if (_parentNO != null && _parentNO.IsSpawned)
                    return _parentNO.OwnerId;
                return -1;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}