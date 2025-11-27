using UnityEngine;

namespace Gameplay
{
    public class BaseArea : MonoBehaviour
    {
        [SerializeField] private float radius = 6f;
        [SerializeField] private int ownerActorNumber = -1;

        public float Radius => radius;
        public int Owner => ownerActorNumber;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
