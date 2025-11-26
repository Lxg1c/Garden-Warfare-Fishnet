using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    public class PlayerMovement : NetworkBehaviour
    {
        public float moveSpeed = 5f;
        private Vector2 _currentMovementInput;

        public override void OnStartClient()
        {
            if (IsOwner)
                GetComponent<PlayerInput>().enabled = true;
        }

        public void OnMove(InputValue value)
        {
            _currentMovementInput = value.Get<Vector2>();
        }

        void Update()
        {
            // Only run this code on the object the local client owns.
            // This prevents us from moving other players' objects.
            if (!IsOwner)
                return;

            Vector3 moveDirection = new Vector3(_currentMovementInput.x, 0f, _currentMovementInput.y);
            if (moveDirection.magnitude > 1f)
                moveDirection.Normalize();

            transform.position += moveSpeed * Time.deltaTime * moveDirection;
        }
    }
}
