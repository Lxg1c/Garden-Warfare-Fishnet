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
        [SerializeField] private float jumpForce = 5f;

        public float normalSpeed = 5f;
        public float carrySpeed = 3f;

        private CarryPlantAgent _carry;

        // Плавное смешивание анимаций 
        private float _animMoveX;
        private float _animMoveY;
        private readonly float _animationSmooth = 10f;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();
            _carry = GetComponent<CarryPlantAgent>();

            _playerInputActions = new PlayerInputActions();
            _playerInputActions.Player.Move.performed += context => _moveInput = context.ReadValue<Vector2>();
            _playerInputActions.Player.Move.canceled += _ => _moveInput = Vector2.zero;

            // Проверка на прыжок
            if (_playerInputActions.Player.Jump != null)
                _playerInputActions.Player.Jump.performed += _ => JumpHandler();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (base.IsOwner)
            {
                _playerInputActions.Enable();
                Debug.Log("CLIENT: Управление включено.");
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (base.IsOwner) _playerInputActions.Disable();
        }

        private void Update()
        {
            if (!base.IsOwner) return;

            ProcessMovement();
            ApplyGravity();
            UpdateAnimator();
        }

        private void ProcessMovement()
        {
            // Формируем вектор движения относительно мира
            // (Если нужно относительно камеры, здесь нужна доп. логика)
            Vector3 inputDir = new Vector3(_moveInput.x, 0, _moveInput.y);

            if (inputDir.magnitude > 0.1f)
            {
                // Поворачиваем персонажа в сторону движения
                // ВАЖНО: Если у вас Blend Tree типа "Strafe" (ходьба боком), закомментируйте блок ниже!
                // Если Blend Tree типа "Run Forward" (бег туда, куда смотришь), оставьте.
                Quaternion targetRotation = Quaternion.LookRotation(inputDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);

                float currentSpeed = (_carry != null && _carry.IsCarrying) ? carrySpeed : normalSpeed;
                _characterController.Move(currentSpeed * Time.deltaTime * inputDir);
            }

            // Вертикальное движение
            _characterController.Move(new Vector3(0, _verticalVelocity, 0) * Time.deltaTime);
        }

        private void UpdateAnimator()
        {
            Vector3 worldMove = new Vector3(_moveInput.x, 0, _moveInput.y);

            // Преобразуем мировое движение в локальное относительно поворота игрока
            Vector3 localMove = transform.InverseTransformDirection(worldMove);

            // Интерполяция
            _animMoveX = Mathf.Lerp(_animMoveX, localMove.x, Time.deltaTime * _animationSmooth);
            _animMoveY = Mathf.Lerp(_animMoveY, localMove.z, Time.deltaTime * _animationSmooth);

            // Установка параметров
            _animator.SetFloat("moveX", _animMoveX);
            _animator.SetFloat("moveY", _animMoveY);

            // Speed берем из ввода, чтобы анимация не зависела от столкновения со стеной
            _animator.SetFloat("speed", worldMove.magnitude);
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

        private void JumpHandler()
        {
            if (!base.IsOwner) return;
            if (_characterController.isGrounded)
            {
                _verticalVelocity = jumpForce;
                // Если есть триггер прыжка
                // _animator.SetTrigger("Jump"); 
                // Не забудьте добавить "Jump" в список параметров NetworkAnimator!
            }
        }
    }
}