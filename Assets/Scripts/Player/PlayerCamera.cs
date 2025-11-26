using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player 
{
    public class PlayerCamera : NetworkBehaviour
    {
        [SerializeField] private float rotationSpeed = 5f;
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private int raycastFrameSkip = 1; 
        
        private Camera _camera;
        private float _currentAngle;
        private int _frameCount;
        private Vector3 _lastTargetDirection;
        
        public override void OnStartClient()
        {
            base.OnStartClient();
            _camera = Camera.main;
            
            if (!IsOwner)
            {
                enabled = false;
                return;
            }
            
            _currentAngle = transform.eulerAngles.y;
        }

        private void Update()
        {
            if (!IsOwner) return;
            
            _frameCount++;
            if (_frameCount % raycastFrameSkip == 0)
            {
                CalculateTargetDirection();
            }
            
            ApplySmoothRotation();
        }

        private void CalculateTargetDirection()
        {
            Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
            Ray ray = _camera.ScreenPointToRay(mouseScreenPos);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
            {
                Vector3 targetDir = hit.point - transform.position;
                targetDir.y = 0;
                
                if (targetDir.sqrMagnitude > 0.01f)
                {
                    _lastTargetDirection = targetDir.normalized;
                }
            }
        }

        private void ApplySmoothRotation()
        {
            if (_lastTargetDirection.sqrMagnitude < 0.01f) return;
            
            float targetAngle = FastAtan2(_lastTargetDirection.x, _lastTargetDirection.z);
            
            float angleDelta = Mathf.DeltaAngle(_currentAngle, targetAngle);
            float smoothDelta = angleDelta * rotationSpeed * Time.deltaTime;
            
            _currentAngle += smoothDelta;
            transform.rotation = Quaternion.Euler(0f, _currentAngle, 0f);
        }

        private float FastAtan2(float y, float x)
        {
            if (x == 0f && y == 0f) return 0f;
            
            float angle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            return angle;
        }
    }
}