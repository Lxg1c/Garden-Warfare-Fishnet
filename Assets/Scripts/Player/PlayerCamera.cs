using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

namespace Player 
{
    public class PlayerCamera : NetworkBehaviour
    {
        [Header("Camera Settings")]
        [SerializeField] private float rotationSpeed = 5f;
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private int raycastFrameSkip = 1; 
        
        [Header("Cinemachine")]
        [SerializeField] private CinemachineCamera playerVirtualCamera;
        
        private Camera _playerCameraComponent;
        private float _currentAngle;
        private int _frameCount;
        private Vector3 _lastTargetDirection;
        
        public override void OnStartClient()
        {
            base.OnStartClient();
            
            if (!IsOwner)
            {
                // Отключаем виртуальную камеру у других игроков
                if (playerVirtualCamera != null)
                {
                    playerVirtualCamera.enabled = false;
                    playerVirtualCamera.Priority.Value = 0; // Новая версия использует Priority.Value
                }
                    
                enabled = false;
                return;
            }
            
            SetupCameraForOwner();
            _currentAngle = transform.eulerAngles.y;
        }

        private void SetupCameraForOwner()
        {
            if (playerVirtualCamera != null)
            {
                // Включаем виртуальную камеру только для владельца
                playerVirtualCamera.enabled = true;
                playerVirtualCamera.Priority.Value = 10; // Новый синтаксис
                
                // Получаем реальную камеру из виртуальной
                _playerCameraComponent = playerVirtualCamera.GetComponent<Camera>();
                
                if (_playerCameraComponent == null)
                {
                    Debug.LogError("Camera component not found on CinemachineCamera!");
                }
                else
                {
                    Debug.Log($"Player camera setup: {_playerCameraComponent.name}");
                }
            }
            else
            {
                Debug.LogError("Player Virtual Camera not assigned!");
            }
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
            // Используем камеру игрока, а не Camera.main
            Camera cameraToUse = _playerCameraComponent != null ? _playerCameraComponent : Camera.main;
            if (cameraToUse == null) 
            {
                Debug.LogWarning("No camera available for raycast!");
                return;
            }
            
            Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
            Ray ray = cameraToUse.ScreenPointToRay(mouseScreenPos);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
            {
                Vector3 targetDir = hit.point - transform.position;
                targetDir.y = 0;
                
                if (targetDir.sqrMagnitude > 0.01f)
                {
                    _lastTargetDirection = targetDir.normalized;
                }
            }
            else
            {
                // Если рейкаст не попал в groundLayer, используем направление от камеры
                Vector3 mouseWorldPos = cameraToUse.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, cameraToUse.nearClipPlane));
                Vector3 targetDir = mouseWorldPos - transform.position;
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
            
            float targetAngle = Mathf.Atan2(_lastTargetDirection.x, _lastTargetDirection.z) * Mathf.Rad2Deg;
            
            float angleDelta = Mathf.DeltaAngle(_currentAngle, targetAngle);
            float smoothDelta = angleDelta * rotationSpeed * Time.deltaTime;
            
            _currentAngle += smoothDelta;
            transform.rotation = Quaternion.Euler(0f, _currentAngle, 0f);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner && playerVirtualCamera != null)
            {
                playerVirtualCamera.enabled = false;
                playerVirtualCamera.Priority.Value = 0;
            }
        }

        // Для дебага - отображаем луч в Scene view
        private void OnDrawGizmos()
        {
            if (!IsOwner || _lastTargetDirection.sqrMagnitude < 0.01f) return;
            
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, _lastTargetDirection * 3f);
            
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position + _lastTargetDirection * 3f, 0.2f);
        }
    }
}