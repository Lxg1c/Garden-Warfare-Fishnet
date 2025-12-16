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
        private PlayerInfo _playerInfo;

        private float _verticalVelocity;
        private Vector2 _moveInput;

        [SerializeField] private float gravityScale = 9.81f;

        public float normalSpeed = 5f;
        public float carrySpeed = 3f;

        // Анимация
        private float _animMoveX;
        private float _animMoveY;
        private readonly float _animationSmooth = 10f;

        // Хеши параметров аниматора
        private int _moveXHash;
        private int _moveYHash;
        private int _moveSpeedHash;

        // Флаг для определения первого движения
        private bool _spawnPositionSet;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();
            _playerInfo = GetComponent<PlayerInfo>();

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

            // Проверяем что CharacterController включен (может быть отключен при смерти)
            if (_characterController == null || !_characterController.enabled) return;

            ProcessMovement();
            ApplyGravity();
            UpdateAnimator();
        }

        private void ProcessMovement()
        {
            Vector3 inputDir = new Vector3(_moveInput.x, 0, _moveInput.y);
            Vector3 moveVector = Vector3.zero;

            if (inputDir.magnitude > 0.1f)
            {
                // При первом движении/повороте - сохраняем позицию спавна
                if (!_spawnPositionSet)
                {
                    SetSpawnPositionOnFirstMove();
                }

                Quaternion targetRotation = Quaternion.LookRotation(inputDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);

                float currentSpeed = normalSpeed;
                moveVector = currentSpeed * Time.deltaTime * inputDir;
            }

            // Применяем гравитацию к движению
            moveVector.y = _verticalVelocity * Time.deltaTime;
            _characterController.Move(moveVector);
        }

        /// <summary>
        /// Сохраняет текущую позицию как точку респавна при первом движении игрока
        /// </summary>
        private void SetSpawnPositionOnFirstMove()
        {
            _spawnPositionSet = true;

            // Отправляем на сервер запрос на сохранение позиции спавна
            SetSpawnPositionServerRpc(transform.position, transform.rotation);

            Debug.Log($"[PlayerMovement] First move detected - setting spawn position at {transform.position}");
        }

        [ServerRpc]
        private void SetSpawnPositionServerRpc(Vector3 position, Quaternion rotation)
        {
            if (_playerInfo == null)
            {
                _playerInfo = GetComponent<PlayerInfo>();
            }

            if (_playerInfo != null)
            {
                _playerInfo.SetSpawnPosition(position, rotation);
                Debug.Log($"[Server] Player {OwnerId} spawn position set to {position}");
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