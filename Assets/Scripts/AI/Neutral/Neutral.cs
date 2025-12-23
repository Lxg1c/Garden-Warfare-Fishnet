using System;
using AI.Camp;
using UnityEngine;
using UnityEngine.AI;
using Core.Components;
using FishNet.Object;

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

        private AiState _state = AiState.Idle;

        private Transform _target;
        private float _attackTimer;
        private float _chaseTimer;
        private float _lastDetectionCheck;
        
        public event Action<Transform> OnNeutralGetDamage;

        private Camp.Camp _campManager;
        private CampController _campController;

        public void SetCampManager(Camp.Camp camp)
        {
            _campManager = camp;
            _campController = GetComponentInParent<CampController>();
        }

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>(); // ВАЖНО: инициализация здоровья!

            _homePosition = transform.position;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            
            if (_health != null)
            {
                _health.OnDamaged += OnDamaged;
                _health.OnDeath += OnDeath;
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            
            if (_health != null)
            {
                _health.OnDamaged -= OnDamaged;
                _health.OnDeath -= OnDeath;
            }
        }

        private void Update()
        {
            if (!IsServerStarted) return; // AI работает только на сервере
            
            switch (_state)
            {
                case AiState.Idle:
                    IdleUpdate();
                    break;

                case AiState.Chasing:
                    ChasingUpdate();
                    break;

                case AiState.Attacking:
                    AttackingUpdate();
                    break;

                case AiState.Returning:
                    ReturningUpdate();
                    break;
            }
        }

        // ------------------------------------------------------------
        // STATE MACHINE
        // ------------------------------------------------------------

        private void IdleUpdate()
        {
            if (Time.time - _lastDetectionCheck > 0.3f)
            {
                _lastDetectionCheck = Time.time;
                DetectPlayers();
            }
        }

        private void ChasingUpdate()
        {
            if (_target == null)
            {
                StartReturn();
                return;
            }

            float dist = Vector3.Distance(transform.position, _target.position);

            if (dist < attackRange)
            {
                SetState(AiState.Attacking);
                return;
            }

            if (dist > chaseRange * 1.5f)
            {
                StartReturn();
                return;
            }

            _chaseTimer -= Time.deltaTime;
            if (_chaseTimer <= 0f)
            {
                StartReturn();
                return;
            }

            _agent.SetDestination(_target.position);
        }

        private void AttackingUpdate()
        {
            if (_target == null)
            {
                StartReturn();
                return;
            }

            float dist = Vector3.Distance(transform.position, _target.position);

            if (dist > attackRange)
            {
                SetState(AiState.Chasing);
                return;
            }

            RotateTowards(_target.position);

            _attackTimer -= Time.deltaTime;

            if (_attackTimer <= 0f)
            {
                Attack();
                _attackTimer = attackCooldown;
            }
        }

        private void ReturningUpdate()
        {
            float dist = Vector3.Distance(transform.position, _homePosition);

            if (dist <= 0.5f)
            {
                SetState(AiState.Idle);
                return;
            }

            _agent.SetDestination(_homePosition);
        }

        // ------------------------------------------------------------
        // ACTIONS
        // ------------------------------------------------------------

        private void SetState(AiState newState)
        {
            if (_state == newState) return;

            _state = newState;

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
                    break;

                case AiState.Returning:
                    _agent.isStopped = false;
                    _target = null;
                    break;
            }
        }

        private void StartReturn()
        {
            SetState(AiState.Returning);
            _agent.SetDestination(_homePosition);
        }

        private void Attack()
        {
            if (_target == null) return;

            Health h = _target.GetComponent<Health>();
            if (h != null)
            {
                h.TakeDamage(attackDamage, transform);
            }
        }

        private void DetectPlayers()
        {
            if (_state != AiState.Idle) return;

            Collider[] hits = Physics.OverlapSphere(transform.position, detectionRange, playerLayer);

            foreach (var c in hits)
            {
                Transform t = c.transform;

                if (t == transform) continue;

                Health h = t.GetComponent<Health>();
                
                bool isAlive = (h != null && h.GetHealth() > 0);
                
                if (!isAlive) continue;

                if (CanSee(t))
                {
                    Aggro(t);
                    return;
                }
            }
        }

        private bool CanSee(Transform t)
        {
            Vector3 dir = t.position - transform.position;
            if (Physics.Raycast(transform.position, dir.normalized, out var hit, dir.magnitude))
                return hit.transform == t;

            return true;
        }

        private void Aggro(Transform t)
        {
            _target = t;
            SetState(AiState.Chasing);
        }

        public void SetAggro(Transform t)
        {
            // НЕ агримся если возвращаемся домой
            if (_state == AiState.Returning) return;

            Aggro(t);
        }

        private void RotateTowards(Vector3 pos)
        {
            Vector3 dir = (pos - transform.position).normalized;
            dir.y = 0;

            if (dir != Vector3.zero)
            {
                Quaternion rot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 5f);
            }
        }

        // ------------------------------------------------------------
        // DAMAGE HANDLING
        // ------------------------------------------------------------

        private void OnDamaged(Transform attacker)
        {
            if (attacker == null) return;

            // НЕ агримся если возвращаемся домой
            if (_state == AiState.Returning) return;

            _target = attacker;
            SetState(AiState.Chasing);

            // Вызываем событие для распространения агро через CampController
            OnNeutralGetDamage?.Invoke(attacker);
        }

        private void OnDeath()
        {
            _agent.isStopped = true;
            enabled = false;

            // Уведомляем Camp ТОЛЬКО на сервере
            if (IsServerStarted)
            {
                if (_campManager != null)
                {
                    _campManager.RemoveUnit(this);
                }

                // Деспавним через NetworkObject
                StartCoroutine(DelayedDespawn());
            }
        }
        
        private System.Collections.IEnumerator DelayedDespawn()
        {
            yield return new WaitForSeconds(2f);
            
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                ServerManager.Despawn(NetworkObject);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, chaseRange);

            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, maxDistanceFromHome);
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