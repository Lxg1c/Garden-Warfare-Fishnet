using Core.Components;
using UnityEngine;
using UnityEngine.AI;
using FishNet.Object;
using System.Collections;


namespace AI.Neutral
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class Neutral : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float chaseRange = 10f;
        [SerializeField] private float detectionRange = 8f;
        [SerializeField] private float attackCooldown = 1f;
        [SerializeField] private int attackDamage = 10;
        [SerializeField] private LayerMask playerLayer;
        [SerializeField] private float returnToHomeTimer = 3f;
        [SerializeField] private float maxDistanceFromHome = 15f;

        private Health _health;
        private NavMeshAgent _agent;
        private Vector3 _homePosition;
        private Camp.Camp _assignedCamp;
        
        // Состояние
        private AiState _currentState = AiState.Idle;

        // Таймеры и цели
        private Transform _target;
        private float _attackTimer;
        private float _chaseTimer; 
        private Coroutine _returnCoroutine;
        private float _lastDetectionTime;
        private readonly float _detectionCheckInterval = 0.5f;
        
        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();

            _homePosition = transform.position;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _homePosition = transform.position;
            
            _health.OnDamaged += OnDamaged;
            _health.OnDeath += OnDeath;
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.OnDamaged -= OnDamaged;
                _health.OnDeath -= OnDeath;
            }
            
            if (_returnCoroutine != null)
                StopCoroutine(_returnCoroutine);
        }

        private void Update()
        {
            if (!IsServerInitialized) return;
            
            if (_currentState == AiState.Chasing || _currentState == AiState.Attacking)
            {
                _chaseTimer -= Time.deltaTime;
                
                if (_chaseTimer <= 0f)
                {
                    StartReturningHome();
                    return;
                }
            }
            
            if (_currentState == AiState.Idle && Time.time - _lastDetectionTime > _detectionCheckInterval)
            {
                CheckForNearbyPlayers();
                _lastDetectionTime = Time.time;
            }
            
            if (_currentState != AiState.Returning && 
                Vector3.Distance(transform.position, _homePosition) > maxDistanceFromHome)
            {
                StartReturningHome();
                return;
            }

            switch (_currentState)
            {
                case AiState.Chasing:
                    UpdateChasing();
                    break;
                case AiState.Attacking:
                    UpdateAttacking();
                    break;
                case AiState.Returning:
                    UpdateReturning();
                    break;
            }
        }

        #region State Updates

        private void UpdateChasing()
        {
            if (_target == null || _target == transform)
            {
                StartReturningHome();
                return;
            }
            
            Health targetHealth = _target.GetComponent<Health>();
            if (targetHealth != null && targetHealth.GetHealth() <= 0)
            {
                StartReturningHome();
                return;
            }

            float distanceToTarget = Vector3.Distance(transform.position, _target.position);
            
            if (distanceToTarget <= attackRange)
            {
                SetState(AiState.Attacking);
            }
            else if (distanceToTarget > chaseRange * 1.5f)
            {
                StartReturningHome();
            }
            else
            {
                _agent.SetDestination(_target.position);
            }
        }

        private void UpdateAttacking()
        {
            if (_target == null || _target == transform)
            {
                StartReturningHome();
                return;
            }
            
            Health targetHealth = _target.GetComponent<Health>();
            if (targetHealth != null && targetHealth.GetHealth() <= 0)
            {
                StartReturningHome();
                return;
            }
            
            Vector3 direction = (_target.position - transform.position).normalized;
            direction.y = 0;
            if (direction != Vector3.zero)
            {
                Quaternion lookRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
            }

            float distanceToTarget = Vector3.Distance(transform.position, _target.position);
            
            if (distanceToTarget > attackRange)
            {
                SetState(AiState.Chasing);
            }
            else
            {
                _attackTimer -= Time.deltaTime;
                if (_attackTimer <= 0f)
                {
                    AttackTarget();
                    _attackTimer = attackCooldown;
                }
            }
        }

        private void UpdateReturning()
        {
            float distanceToHome = Vector3.Distance(transform.position, _homePosition);

            if (distanceToHome <= 0.5f)
            {
                SetState(AiState.Idle);
            }
            else
            {
                _agent.SetDestination(_homePosition);
            }
        }

        #endregion

        #region Actions

        [Server]
        private void AttackTarget()
        {
            if (_target == null || _target == transform) return;

            Debug.Log($"{name} attacks {_target.name} for {attackDamage} damage!");
            
            Health targetHealth = _target.GetComponent<Health>();
            if (targetHealth != null)
            {
                targetHealth.TakeDamage(attackDamage, transform);
            }
        }

        [Server]
        public void SetState(AiState newState)
        {
            if (_currentState == newState) return;

            Debug.Log($"{name}: {_currentState} -> {newState}");
            _currentState = newState;
    
            switch (newState)
            {
                case AiState.Idle:
                    _agent.isStopped = true;
                    _target = null;
                    break;

                case AiState.Chasing:
                    _agent.isStopped = false;
                    _chaseTimer = returnToHomeTimer; 
                    break;

                case AiState.Attacking:
                    _agent.isStopped = true;
                    _attackTimer = 0f;
                    _chaseTimer = returnToHomeTimer;
                    break;

                case AiState.Returning:
                    _agent.isStopped = false;
                    _target = null;
                    _chaseTimer = 0f;
                    break;
            }
        }

        #endregion

        #region Detection and Aggro

        /// <summary>
        /// Проверка близких игроков (для автоматического агро)
        /// </summary>
        private void CheckForNearbyPlayers()
        {
            if (_currentState != AiState.Idle) return;

            Collider[] players = Physics.OverlapSphere(transform.position, detectionRange, playerLayer);

            foreach (var player in players)
            {
                if (player.transform == transform) continue;
                
                Health targetHealth = player.GetComponent<Health>();
                if (targetHealth != null && targetHealth.GetHealth() <= 0) continue;
                
                if (CanSeeTarget(player.transform))
                {
                    SetAggro(player.transform);
                    return;
                }
            }
        }

        /// <summary>
        /// Проверка видимости цели (raycast)
        /// </summary>
        private bool CanSeeTarget(Transform target)
        {
            if (target == null) return false;
            
            Vector3 direction = target.position - transform.position;
            float distance = direction.magnitude;
            
            // Проверяем Raycast
            if (Physics.Raycast(transform.position, direction.normalized, out RaycastHit hit, distance))
            {
                return hit.transform == target;
            }
            
            return true;
        }

        /// <summary>
        /// Установить агро на цель
        /// </summary>
        [Server]
        public void SetAggro(Transform target)
        {
            if (target == null || target == transform) return;
    
            Health targetHealth = target.GetComponent<Health>();
            if (targetHealth != null && targetHealth.GetHealth() <= 0) return;
    
            _target = target;
            SetState(AiState.Chasing);
    
            Debug.Log($"{name}: Aggro set on {target.name}!");
        }

        /// <summary>
        /// Начать возвращение домой
        /// </summary>
        [Server]
        private void StartReturningHome()
        {
            if (_currentState == AiState.Returning) return;
            
            SetState(AiState.Returning);
            
            if (_returnCoroutine != null)
                StopCoroutine(_returnCoroutine);
            
            _returnCoroutine = StartCoroutine(ReturnHomeRoutine());
        }

        private IEnumerator ReturnHomeRoutine()
        {
            yield return new WaitForSeconds(0.1f);
            
            if (_currentState == AiState.Returning)
            {
                _agent.SetDestination(_homePosition);
            }
        }

        #endregion

        #region Damage Handling

        /// <summary>
        /// Вызывается, когда нейтрал получает урон
        /// </summary>
        private void OnDamaged(Transform attacker)
        {
            if (!IsServerInitialized) return;
            
            if (attacker == transform || attacker == null)
            {
                Debug.LogWarning($"{name}: Received damage from self or null!");
                return;
            }
            
            Health attackerHealth = attacker.GetComponent<Health>();
            if (attackerHealth != null && attackerHealth.GetHealth() <= 0)
            {
                Debug.Log($"{name}: Attacker is dead!");
                return;
            }
            
            _target = attacker;
            SetState(AiState.Chasing);
        }
        
        /// <summary>
        /// Вызывается при смерти нейтрала
        /// </summary>
        private void OnDeath(Transform deadTransform)
        {
            if (!IsServerInitialized) return;
            
            Debug.Log($"{name} died!");
            
            if (_returnCoroutine != null)
                StopCoroutine(_returnCoroutine);
            
            _agent.isStopped = true;
            enabled = false;
        }

        #endregion

        private void OnDrawGizmosSelected()
        {
            // Визуализация радиусов
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, chaseRange);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
            
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, detectionRange);
            
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, maxDistanceFromHome);
            
            // Показываем цель, если есть
            if (_target != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(transform.position, _target.position);
            }
            
            // Показываем точку дома
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(_homePosition, 0.3f);
        }
        
        public AiState GetCurrentState()
        {
            return _currentState;
        }
    }

    public enum AiState
    {
        Idle,
        Chasing,
        Attacking,
        Returning
    }
}
