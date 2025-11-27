using System.Collections;
using Core.Components;
using Core.Interfaces;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace AI.Neutral
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    [DisallowMultipleComponent]
    public class Neutral : NetworkBehaviour, IAttackableZoneHandler
    {
        [Header("Detection Settings")]
        public float detectionRadius = 6f;
        public float attackRange = 4f;
        public float aggressionDuration = 5f;
        public float maxDistanceFromHome = 10f;
        public Transform homePoint;
        public string playerTag = "Player";

        [Header("Group Settings")]
        [FormerlySerializedAs("spawnGroup")]
        public NeutralSpawnPointGroup neutralSpawnGroup;
        private NeutralSpawnPointGroup _assignedGroup;

        [Header("Combat Settings")]
        public float attackDamage = 10f;
        public float attackCooldown = 1f;

        private NavMeshAgent _agent;
        private Health _health;
        private Transform _currentTarget;
        private Vector3 _homePosition;
        private float _lastAttackTime;
        private float _aggroEndTime = 3f;
        private bool _hasAggro;
        private Coroutine _returnCoroutine;
        private float _combatTimer;
        private bool _combatTimerActive;
        private float _lastDetectionTime;

        public string TargetTag => playerTag;

        private enum State { Idle, Chasing, Attacking, Returning }
        private State _currentState = State.Idle;

        public static event System.Action<Transform, Transform> OnGroupAggro;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
        }

        private void Start()
        {
            _homePosition = homePoint != null ? homePoint.position : transform.position;

            // Подписка на события Health
            _health.OnDamaged += OnDamaged;

            _agent.stoppingDistance = 3f;
            _agent.autoBraking = true;

            if (neutralSpawnGroup != null)
                neutralSpawnGroup.RegisterNeutral(this);
            else
            {
                var parentGroup = GetComponentInParent<NeutralSpawnPointGroup>();
                if (parentGroup != null)
                    parentGroup.RegisterNeutral(this);
            }
        }

        public void SetSpawnGroup(NeutralSpawnPointGroup group)
        {
            _assignedGroup = group;
        }

        private void Update()
        {
            // ИСПРАВЛЕНИЕ: Используем IsServerInitialized вместо IsServer
            if (!base.IsServerInitialized) return;

            CheckAttackRange();

            if (Time.time - _lastDetectionTime > 0.5f)
            {
                CheckForNearbyPlayers();
                _lastDetectionTime = Time.time;
            }

            if (_combatTimerActive)
            {
                _combatTimer -= Time.deltaTime;
                if (_combatTimer <= 0f)
                {
                    StartReturningHome();
                    _combatTimerActive = false;
                }
            }

            switch (_currentState)
            {
                case State.Idle: UpdateIdle(); break;
                case State.Chasing: UpdateChasing(); break;
                case State.Attacking: UpdateAttacking(); break;
                case State.Returning: UpdateReturning(); break;
            }
        }

        private void CheckAttackRange()
        {
            if (_hasAggro && _currentTarget != null)
            {
                float dist = Vector3.Distance(transform.position, _currentTarget.position);

                if (_currentState == State.Chasing && dist <= attackRange)
                {
                    _currentState = State.Attacking;
                    _agent.isStopped = true;
                }
                else if (_currentState == State.Attacking && dist > attackRange)
                {
                    _currentState = State.Chasing;
                    _agent.isStopped = false;
                }
            }
        }

        #region FSM States

        private void UpdateIdle()
        {
            float distHome = Vector3.Distance(transform.position, _homePosition);
            if (distHome > maxDistanceFromHome)
            {
                StartReturningHome();
                return;
            }

            if (_hasAggro && _currentTarget != null)
            {
                _currentState = State.Chasing;
                _agent.isStopped = false;
            }
        }

        private void UpdateChasing()
        {
            if (_currentTarget == null || Time.time > _aggroEndTime)
            {
                StartReturningHome();
                return;
            }

            float dist = Vector3.Distance(transform.position, _currentTarget.position);
            if (dist <= attackRange)
            {
                _currentState = State.Attacking;
                _agent.isStopped = true;
            }
            else
            {
                _agent.SetDestination(_currentTarget.position);
            }
        }

        private void UpdateAttacking()
        {
            if (_currentTarget == null || Time.time > _aggroEndTime)
            {
                StartReturningHome();
                return;
            }

            float dist = Vector3.Distance(transform.position, _currentTarget.position);
            if (dist > attackRange)
            {
                _currentState = State.Chasing;
                _agent.isStopped = false;
                return;
            }

            RotateTowardsTarget();

            if (Time.time >= _lastAttackTime + attackCooldown)
                AttackTarget();
        }

        private void UpdateReturning()
        {
            float distHome = Vector3.Distance(transform.position, _homePosition);

            if (distHome <= 1f)
            {
                _agent.isStopped = true;
                _currentState = State.Idle;
                _hasAggro = false;
                _currentTarget = null;

                if (_returnCoroutine != null)
                    StopCoroutine(_returnCoroutine);

                return;
            }

            if (!_agent.hasPath || Vector3.Distance(_agent.destination, _homePosition) > 0.5f)
                _agent.SetDestination(_homePosition);
        }

        #endregion

        #region Combat Logic

        private void AttackTarget()
        {
            if (_currentTarget == null) return;

            var targetHealth = _currentTarget.GetComponent<Health>();
            if (targetHealth == null) return;

            // Наносим урон (выполняется на сервере)
            targetHealth.TakeDamage(attackDamage, transform);

            _lastAttackTime = Time.time;
            _aggroEndTime = Time.time + aggressionDuration;
        }

        private void RotateTowardsTarget()
        {
            if (_currentTarget == null) return;
            Vector3 dir = (_currentTarget.position - transform.position).normalized;
            dir.y = 0;

            if (dir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5f);
            }
        }

        #endregion

        #region Aggro System

        public void OnGroupAggroTriggered(Transform aggroSource, Transform target)
        {
            if (aggroSource == transform) return;

            Health th = target.GetComponent<Health>();
            if (th != null && th.GetHealth() <= 0) return;

            if (_hasAggro && _currentTarget == target)
            {
                _aggroEndTime = Time.time + aggressionDuration;
                _combatTimer = 3f;
                return;
            }

            SetAggro(target);
        }

        private void SetAggro(Transform target)
        {
            if (target == null) return;

            var th = target.GetComponent<Health>();
            if (th != null && th.GetHealth() <= 0) return;

            UnsubscribeFromTargetDeath();
            _currentTarget = target;
            _hasAggro = true;
            _aggroEndTime = Time.time + aggressionDuration;

            SubscribeToTargetDeath();

            _combatTimer = 3f;
            _combatTimerActive = true;

            _currentState = State.Chasing;
            _agent.isStopped = false;

            (_assignedGroup ?? neutralSpawnGroup)?.NotifyGroupAggro(transform, target);
        }

        private void StartReturningHome()
        {
            if (_currentState == State.Returning) return;

            _currentState = State.Returning;
            _hasAggro = false;
            _agent.isStopped = false;
            _agent.SetDestination(_homePosition);

            UnsubscribeFromTargetDeath();
            _combatTimerActive = false;

            if (_returnCoroutine != null)
                StopCoroutine(_returnCoroutine);

            _returnCoroutine = StartCoroutine(ReturnHomeRoutine());
        }

        private IEnumerator ReturnHomeRoutine()
        {
            yield return new WaitForSeconds(0.5f);
            if (_currentState == State.Returning)
                _agent.SetDestination(_homePosition);
        }

        #endregion

        #region Damage & Death

        private void OnDamaged(Transform attacker)
        {
            if (attacker == null) return;

            // Логика Агро только на сервере
            // ИСПРАВЛЕНИЕ: IsServer -> IsServerInitialized
            if (IsServerInitialized)
            {
                SetAggro(attacker);
                OnGroupAggro?.Invoke(transform, attacker);
            }

            // Визуал (покраснение) отправляем всем
            ObserversRpc_DamageFlash();
        }

        [ObserversRpc]
        private void ObserversRpc_DamageFlash()
        {
            StartCoroutine(DamageFlash());
        }

        private IEnumerator DamageFlash()
        {
            var mesh = GetComponent<Renderer>();
            if (mesh == null) yield break;

            Color original = mesh.material.color;
            mesh.material.color = Color.red;
            yield return new WaitForSeconds(0.2f);
            mesh.material.color = original;
        }

        private void OnTargetDied(Transform dead)
        {
            if (_currentTarget == dead)
                StartReturningHome();
        }

        private void SubscribeToTargetDeath()
        {
            if (_currentTarget == null) return;

            var th = _currentTarget.GetComponent<Health>();
            if (th != null) th.OnDeath += OnTargetDied;
        }

        private void UnsubscribeFromTargetDeath()
        {
            if (_currentTarget == null) return;

            var th = _currentTarget.GetComponent<Health>();
            if (th != null) th.OnDeath -= OnTargetDied;
        }

        #endregion

        private void CheckForNearbyPlayers()
        {
            if (_hasAggro || _currentState == State.Returning) return;

            Collider[] hits = new Collider[10];
            int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, hits);

            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.CompareTag(playerTag)) continue;

                var health = hit.GetComponent<Health>();
                if (health != null && health.GetHealth() > 0)
                {
                    SetAggro(hit.transform);
                    break;
                }
            }
        }

        public void OnEnterAttackRange(Transform target)
        {
            if (_hasAggro && _currentTarget == target && _currentState == State.Chasing)
            {
                _currentState = State.Attacking;
                _agent.isStopped = true;
            }
        }

        public void OnExitAttackRange(Transform target)
        {
            if (_hasAggro && _currentTarget == target && _currentState == State.Attacking)
            {
                _currentState = State.Chasing;
                _agent.isStopped = false;
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_health != null)
                _health.OnDamaged -= OnDamaged;

            UnsubscribeFromTargetDeath();

            if (_assignedGroup != null)
                _assignedGroup.UnregisterNeutral(this);
            else if (neutralSpawnGroup != null)
                neutralSpawnGroup.UnregisterNeutral(this);
        }
    }
}