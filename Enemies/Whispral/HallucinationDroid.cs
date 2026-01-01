using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using VepMod.VepFramework;
using VepMod.VepFramework.Structures.FSM;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Hallucination basée sur le prefab LostDroid de WesleysEnemies.
///     Se balade sans attaquer, utilise l'architecture originale:
///     - Controller: NavMeshAgent + CharacterController (mouvement)
///     - Rigidbody: transform suivi par Cube via ParentConstraint
///     - Cube: visuels + Animator
/// </summary>
public sealed class HallucinationDroid : StateMachineComponent<HallucinationDroid, HallucinationDroid.StateId>
{
    private const float RotationSpeed = 10f;
    private const float WalkSpeed = 2f;
    private const float SprintSpeed = 5f;
    private const float SprintChance = 0.5f;

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationDroid>();
    private Animator _animator;
    private CharacterController _charController;

    private Transform _controllerTransform;
    private Vector3 _currentVelocity;

    private Vector3 _destination;
    private NavMeshAgent _navAgent;
    private Transform _rigidbodyTransform;

    private int _savedAgentTypeID;
    private int _savedAreaMask = NavMesh.AllAreas;
    private Quaternion _targetRotation = Quaternion.identity;

    public bool IsWalking { get; set; }
    public bool IsSprinting { get; set; }
    public bool IsTurning { get; private set; }
    public bool HasChangedMovementState { get; set; }
    public PlayerAvatar SourcePlayer { get; private set; }
    public Transform ControllerTransform => _controllerTransform;

    protected override StateId DefaultState => StateId.Idle;

    protected override void Awake()
    {
        // Ne pas appeler base.Awake() ici - on initialise la FSM dans Initialize()
    }

    protected override void Update()
    {
        if (_navAgent == null || !_navAgent.isOnNavMesh) return;
        if (_charController == null || _controllerTransform == null) return;

        // Update FSM
        fsm?.Update();

        UpdateMovement();
        UpdateRotation();
        SyncVisualsToController();
        UpdateAnimationFlags();
        SyncNavAgentPosition();
    }

    #region Animation

    private void UpdateAnimationFlags()
    {
        var currentState = fsm?.CurrentStateStateId;
        if ((currentState == StateId.Wander || currentState == StateId.Sprint) && _controllerTransform != null)
        {
            var angle = Quaternion.Angle(_controllerTransform.rotation, _targetRotation);
            IsTurning = angle > 7f;
        }
        else
        {
            IsTurning = false;
        }

        if (_animator != null)
        {
            _animator.SetBool("isWalking", IsWalking);
            _animator.SetBool("isSprinting", IsSprinting);
            _animator.SetBool("isTurning", IsTurning);
            _animator.SetBool("stun", false);
        }
    }

    #endregion

    #region Movement

    private void UpdateMovement()
    {
        var currentState = fsm?.CurrentStateStateId;
        if ((currentState != StateId.Wander && currentState != StateId.Sprint) || !_navAgent.hasPath)
        {
            _currentVelocity = Vector3.zero;
            return;
        }

        _currentVelocity = Vector3.Lerp(_currentVelocity, _navAgent.desiredVelocity, 5f * Time.deltaTime);

        if (_currentVelocity.magnitude > 0.01f)
        {
            var moveVector = _currentVelocity * Time.deltaTime;
            moveVector.y = -0.5f * Time.deltaTime;
            _charController.Move(moveVector);
        }
    }

    private void SyncVisualsToController()
    {
        if (_rigidbodyTransform == null || _controllerTransform == null) return;

        _rigidbodyTransform.position = _controllerTransform.position;
        _rigidbodyTransform.rotation = _controllerTransform.rotation;

        if (_animator != null && _animator.transform.localPosition != Vector3.zero)
        {
            _animator.transform.localPosition = Vector3.zero;
        }
    }

    private void SyncNavAgentPosition()
    {
        if (_navAgent == null || _controllerTransform == null) return;

        var controllerPos = _controllerTransform.position;

        if (NavMesh.SamplePosition(controllerPos, out var hit, 2f, _savedAreaMask))
        {
            _navAgent.nextPosition = hit.position;

            var currentState = fsm?.CurrentStateStateId;
            if (Vector3.Distance(controllerPos, hit.position) > 0.5f &&
                (currentState == StateId.Wander || currentState == StateId.Sprint) && _navAgent.hasPath)
            {
                _navAgent.SetDestination(_destination);
            }
        }
        else
        {
            LOG.Warning($"Controller off NavMesh at {controllerPos}");
            fsm.NextStateStateId = StateId.Idle;
        }
    }

    private void UpdateRotation()
    {
        if (_controllerTransform == null) return;

        if (_currentVelocity.magnitude > 0.1f)
        {
            var velocityDirection = _currentVelocity.normalized;
            velocityDirection.y = 0f;
            if (velocityDirection.sqrMagnitude > 0.01f)
            {
                _targetRotation = Quaternion.LookRotation(velocityDirection);
            }
        }

        _controllerTransform.rotation = Quaternion.Slerp(
            _controllerTransform.rotation,
            _targetRotation,
            RotationSpeed * Time.deltaTime);
    }

    public bool TrySetRandomDestination()
    {
        var controllerPos = _controllerTransform != null ? _controllerTransform.position : transform.position;

        var levelPoint = SemiFunc.LevelPointGet(controllerPos, 5f, 15f)
                         ?? SemiFunc.LevelPointGet(controllerPos, 0f, 20f);

        if (levelPoint == null) return false;

        var targetPos = levelPoint.transform.position;

        if (!NavMesh.SamplePosition(targetPos, out var hit, 5f, _savedAreaMask))
        {
            return false;
        }

        if (!Physics.Raycast(hit.position + Vector3.up, Vector3.down, 5f, LayerMask.GetMask("Default")))
        {
            return false;
        }

        _destination = hit.position;

        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(_destination, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        _navAgent.SetPath(path);
        return true;
    }

    public void ResetPath()
    {
        if (_navAgent != null && _navAgent.isOnNavMesh)
        {
            _navAgent.ResetPath();
        }
    }

    public bool HasReachedDestination()
    {
        if (_navAgent.pathPending) return false;

        var controllerPos = _controllerTransform != null ? _controllerTransform.position : transform.position;
        var distance = Vector3.Distance(controllerPos, _destination);

        return !_navAgent.hasPath || distance <= _navAgent.stoppingDistance;
    }

    public void SetSpeed(float speed)
    {
        if (_navAgent != null)
        {
            _navAgent.speed = speed;
        }
    }

    /// <summary>
    ///     Joue le son enregistré du joueur source à la position du Controller.
    ///     Utilise le WhispralMimics du joueur LOCAL (qui a les fichiers audio stockés).
    /// </summary>
    public void PlayVoice(bool applyFilter = false)
    {
        if (SourcePlayer == null || _controllerTransform == null) return;

        // Utiliser le WhispralMimics du joueur local (pas du source player)
        var localPlayer = PlayerAvatar.instance;
        if (localPlayer == null) return;

        var mimics = localPlayer.GetComponent<WhispralMimics>();
        if (mimics == null)
        {
            LOG.Warning("WhispralMimics not found on local player");
            return;
        }

        mimics.PlayAudioAtTransform(_controllerTransform, SourcePlayer.playerName, applyFilter);
    }

    #endregion

    #region Factory & Initialization

    public static HallucinationDroid Create(PlayerAvatar sourcePlayer, Vector3 spawnPosition)
    {
        if (!LostDroidPrefabLoader.IsAvailable)
        {
            LOG.Warning("LostDroid prefab not available");
            return null;
        }

        var instance = Instantiate(LostDroidPrefabLoader.LostDroidPrefab, spawnPosition, Quaternion.identity);
        if (instance == null)
        {
            LOG.Error("Failed to instantiate LostDroid prefab");
            return null;
        }

        instance.name = $"HallucinationDroid_{sourcePlayer.playerName}";

        var hallucination = instance.AddComponent<HallucinationDroid>();
        hallucination.Initialize(sourcePlayer);

        return hallucination;
    }

    private void Initialize(PlayerAvatar sourcePlayer)
    {
        SourcePlayer = sourcePlayer;

        FindCriticalTransforms();
        SaveNavMeshSettings();
        DisableEnemyComponents();
        SetupNavigation();
        SetupAnimation();
        InitializeFSM();

        LOG.Info($"HallucinationDroid created for {sourcePlayer.playerName} at {_controllerTransform?.position}");
    }

    private void InitializeFSM()
    {
        fsm = new StateMachine(this, DefaultState);
        fsm.AddState(StateId.Idle, new IdleState());
        fsm.AddState(StateId.Wander, new WanderState());
        fsm.AddState(StateId.Sprint, new SprintState());
    }

    private void FindCriticalTransforms()
    {
        _controllerTransform = transform.Find("Controller");
        if (_controllerTransform == null)
        {
            foreach (var child in GetComponentsInChildren<Transform>())
            {
                if (child.name == "Controller")
                {
                    _controllerTransform = child;
                    break;
                }
            }
        }

        if (_controllerTransform == null)
        {
            LOG.Warning("Controller transform not found, creating one");
            var controllerGO = new GameObject("Controller");
            controllerGO.transform.SetParent(transform);
            controllerGO.transform.localPosition = Vector3.zero;
            _controllerTransform = controllerGO.transform;
        }

        _rigidbodyTransform = transform.Find("Rigidbody");
        if (_rigidbodyTransform == null)
        {
            foreach (var child in GetComponentsInChildren<Transform>())
            {
                if (child.name == "Rigidbody")
                {
                    _rigidbodyTransform = child;
                    break;
                }
            }
        }

        if (_rigidbodyTransform == null)
        {
            LOG.Warning("Rigidbody transform not found");
        }
    }

    private void SaveNavMeshSettings()
    {
        NavMeshAgent existingAgent = null;

        if (_controllerTransform != null)
        {
            existingAgent = _controllerTransform.GetComponent<NavMeshAgent>();
        }

        if (existingAgent == null)
        {
            existingAgent = GetComponentInChildren<NavMeshAgent>();
        }

        if (existingAgent != null)
        {
            _savedAgentTypeID = existingAgent.agentTypeID;
            _savedAreaMask = existingAgent.areaMask;
        }
        else
        {
            _savedAgentTypeID = -334000983;
            _savedAreaMask = 1;
        }
    }

    private void DisableEnemyComponents()
    {
        var componentsToDestroy = new List<Component>();
        var componentsToDisable = new List<MonoBehaviour>();

        foreach (var component in GetComponentsInChildren<Component>(true))
        {
            if (component == null || component == this) continue;

            var typeName = component.GetType().Name;
            var fullTypeName = component.GetType().FullName ?? "";

            if (component is Transform) continue;
            if (component is Animator) continue;
            if (component is Renderer) continue;
            if (component is MeshFilter) continue;
            if (component is SkinnedMeshRenderer) continue;
            if (component is NavMeshAgent) continue;

            if (fullTypeName.Contains("Photon") || typeName.Contains("Photon") ||
                fullTypeName.Contains("Enemy") || fullTypeName.Contains("LostDroid") ||
                typeName.Contains("Enemy") || typeName.Contains("LostDroid"))
            {
                componentsToDestroy.Add(component);
                continue;
            }

            if (component is MonoBehaviour mb)
            {
                componentsToDisable.Add(mb);
            }
        }

        for (var pass = 0; pass < 5; pass++)
        {
            var destroyedThisPass = 0;
            foreach (var component in componentsToDestroy)
            {
                if (component != null)
                {
                    try
                    {
                        DestroyImmediate(component);
                        destroyedThisPass++;
                    }
                    catch { }
                }
            }

            if (destroyedThisPass == 0) break;
        }

        foreach (var mb in componentsToDisable)
        {
            if (mb != null && mb != this)
            {
                mb.enabled = false;
            }
        }

        foreach (var rb in GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        foreach (var col in GetComponentsInChildren<Collider>())
        {
            col.isTrigger = true;
        }
    }

    private void SetupAnimation()
    {
        _animator = GetComponentInChildren<Animator>();

        if (_animator != null)
        {
            _animator.enabled = true;
            _animator.applyRootMotion = false;

            var parentConstraint = _animator.GetComponent<ParentConstraint>();
            if (parentConstraint != null && !parentConstraint.constraintActive)
            {
                parentConstraint.constraintActive = true;
            }
        }
        else
        {
            LOG.Warning("No Animator found on prefab");
        }
    }

    private void SetupNavigation()
    {
        if (_controllerTransform == null)
        {
            LOG.Error("Cannot setup navigation: Controller is null");
            return;
        }

        _navAgent = _controllerTransform.GetComponent<NavMeshAgent>();
        if (_navAgent == null)
        {
            _navAgent = _controllerTransform.gameObject.AddComponent<NavMeshAgent>();
        }

        var startPos = _controllerTransform.position;
        var validPosition = startPos;
        var foundNavMesh = false;

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = _savedAgentTypeID,
            areaMask = _savedAreaMask
        };

        float[] distances = { 5f, 10f, 20f, 50f };
        foreach (var distance in distances)
        {
            if (NavMesh.SamplePosition(startPos, out var hit, distance, filter))
            {
                validPosition = hit.position;
                foundNavMesh = true;
                break;
            }
        }

        if (!foundNavMesh)
        {
            LOG.Error($"No NavMesh found near {startPos}");
            return;
        }

        _navAgent.enabled = false;
        _navAgent.agentTypeID = _savedAgentTypeID;
        _navAgent.areaMask = _savedAreaMask;

        _navAgent.baseOffset = 0f;
        _navAgent.speed = 2f;
        _navAgent.angularSpeed = 999f;
        _navAgent.acceleration = 15f;
        _navAgent.stoppingDistance = 0f;
        _navAgent.autoBraking = true;

        _navAgent.updatePosition = false;
        _navAgent.updateRotation = false;

        _navAgent.radius = 0.7f;
        _navAgent.height = 2f;
        _navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        _navAgent.avoidancePriority = 10;

        _navAgent.autoTraverseOffMeshLink = true;
        _navAgent.autoRepath = true;

        _controllerTransform.position = validPosition;
        _navAgent.Warp(validPosition);
        _navAgent.enabled = true;

        SetupCharacterController();
    }

    private void SetupCharacterController()
    {
        if (_controllerTransform == null) return;

        var existingCC = _controllerTransform.GetComponent<CharacterController>();
        if (existingCC != null)
        {
            DestroyImmediate(existingCC);
        }

        _charController = _controllerTransform.gameObject.AddComponent<CharacterController>();
        _charController.height = 2f;
        _charController.radius = 0.5f;
        _charController.center = new Vector3(0f, 1f, 0f);
        _charController.slopeLimit = 45f;
        _charController.stepOffset = 0.3f;
        _charController.skinWidth = 0.08f;
        _charController.minMoveDistance = 0.001f;
    }

    #endregion

    #region FSM

    public enum StateId
    {
        Idle,
        Wander,
        Sprint
    }

    private class IdleState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float MinIdleTime = 10f;
        private const float MaxIdleTime = 15f;

        private float _duration;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _duration = Random.Range(MinIdleTime, MaxIdleTime);
            Machine.Owner.ResetPath();
            Machine.Owner.HasChangedMovementState = false;
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (TimeElapsed >= _duration)
            {
                // Chance aléatoire de courir au lieu de marcher
                Machine.NextStateStateId = Random.value < SprintChance ? StateId.Sprint : StateId.Wander;
            }
        }
    }

    private abstract class MovementState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float SwitchCheckInterval = 1f;
        private const float SwitchChance = 0.2f;

        private float _duration;
        private float _nextSwitchCheck;

        protected abstract float MinDuration { get; }
        protected abstract float MaxDuration { get; }
        protected abstract float Speed { get; }
        protected abstract StateId SwitchTargetState { get; }
        protected abstract void SetMovementFlag(bool value);

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _duration = Random.Range(MinDuration, MaxDuration);
            _nextSwitchCheck = SwitchCheckInterval;

            if (!Machine.Owner.TrySetRandomDestination())
            {
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            Machine.Owner.SetSpeed(Speed);
            SetMovementFlag(true);
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            SetMovementFlag(false);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (Machine.Owner.HasReachedDestination() || TimeElapsed >= _duration)
            {
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            // 20% de chance de changer d'état (une seule fois)
            if (!Machine.Owner.HasChangedMovementState && TimeElapsed >= _nextSwitchCheck)
            {
                _nextSwitchCheck += SwitchCheckInterval;
                if (Random.value < SwitchChance)
                {
                    Machine.Owner.HasChangedMovementState = true;
                    Machine.NextStateStateId = SwitchTargetState;
                }
            }
        }
    }

    private class WanderState : MovementState
    {
        protected override float MinDuration => 5f;
        protected override float MaxDuration => 15f;
        protected override float Speed => WalkSpeed;
        protected override StateId SwitchTargetState => StateId.Sprint;
        protected override void SetMovementFlag(bool value) => Machine.Owner.IsWalking = value;
    }

    private class SprintState : MovementState
    {
        protected override float MinDuration => 3f;
        protected override float MaxDuration => 8f;
        protected override float Speed => SprintSpeed;
        protected override StateId SwitchTargetState => StateId.Wander;
        protected override void SetMovementFlag(bool value) => Machine.Owner.IsSprinting = value;
    }

    #endregion
}