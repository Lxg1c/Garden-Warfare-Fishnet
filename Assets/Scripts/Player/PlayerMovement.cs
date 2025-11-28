using Core.Spawn;
using FischlWorks_FogWar;
using UnityEngine;
using FishNet.Object;
using Player.Components;

namespace Player
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    public class PlayerMovement : NetworkBehaviour
    {
        private PlayerInputActions _playerInputActions;
        private CharacterController _characterController;
        private Animator _animator;

        private float _verticalVelocity;
        private Vector2 _moveInput;

        [SerializeField] private float gravityScale = 9.81f;

        public float normalSpeed = 5f;
        public float carrySpeed = 3f;

        private CarryPlantAgent _carry;
        
        // Fog of war
        public FogOfWarController fogController;
        private csFogVisibilityAgent _visibilityAgent;
        
        // Анимации
        private float _animMoveX;
        private float _animMoveY;
        private readonly float _animationSmooth = 10f;
        
        // Кэшированные хэши параметров
        private int _moveXHash;
        private int _moveYHash;
        private int _moveSpeedHash;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();
            _carry = GetComponent<CarryPlantAgent>();
            _visibilityAgent = GetComponent<csFogVisibilityAgent>();
            
            _moveXHash = Animator.StringToHash("moveX");
            _moveYHash = Animator.StringToHash("moveY");
            _moveSpeedHash = Animator.StringToHash("speed");

            _playerInputActions = new PlayerInputActions();
            _playerInputActions.Player.Move.performed += context => _moveInput = context.ReadValue<Vector2>();
            _playerInputActions.Player.Move.canceled += _ => _moveInput = Vector2.zero;
        }


        public override void OnStartClient()
        {
            base.OnStartClient();

            if (IsOwner)
            {
                _playerInputActions.Enable();
                
                if (fogController == null)
                {
                    fogController = FindFirstObjectByType<FogOfWarController>();
                }
                
                if (fogController != null)
                {
                    fogController.InitializeForPlayer(transform, 6);
                    
                    csFogWar fogInstance = fogController.GetFogInstance();
                    if (fogInstance != null && _visibilityAgent != null)
                    {
                        _visibilityAgent.SetFogWar(fogInstance);
                        Debug.Log("Fog of war instance set to visibility agent");
                    }
                    else
                    {
                        Debug.LogWarning("Failed to set fog war to visibility agent");
                    }
                }
                else
                {
                    Debug.LogError("FogOfWarController not found!");
                }

                Debug.Log("CLIENT: Управление включено + локальный FOW создан.");
            }
        }


        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner) _playerInputActions.Disable();
        }

        private void Update()
        {
            if (!IsOwner) return;

            ProcessMovement();
            ApplyGravity();
            UpdateAnimator();
        }

        private void ProcessMovement()
        {
            Vector3 inputDir = new Vector3(_moveInput.x, 0, _moveInput.y);

            if (inputDir.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(inputDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);

                float currentSpeed = (_carry != null && _carry.IsCarrying) ? carrySpeed : normalSpeed;
                _characterController.Move(currentSpeed * Time.deltaTime * inputDir);
            }
        }

        private void UpdateAnimator()
        {
            Vector3 worldMove = new Vector3(_moveInput.x, 0, _moveInput.y);
            
            Vector3 localMove = transform.InverseTransformDirection(worldMove);
            
            _animMoveX = Mathf.Lerp(_animMoveX, localMove.x, Time.deltaTime * _animationSmooth);
            _animMoveY = Mathf.Lerp(_animMoveY, localMove.z, Time.deltaTime * _animationSmooth);
            
            _animator.SetFloat(_moveXHash, _animMoveX);
            _animator.SetFloat(_moveYHash, _animMoveY);
            
            _animator.SetFloat(_moveSpeedHash, worldMove.magnitude);
        }

        private void ApplyGravity()
        {
            if (_characterController.isGrounded && _verticalVelocity < 0)
            {
                _verticalVelocity = -2f;
            }
            else
            {
                _verticalVelocity -= gravityScale * Time.deltaTime;
            }
        }
    }
}