using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace VepMod.Enemies.Whispral;

internal class LostDroid : MonoBehaviour
{
    public enum State
    {
        Spawn,
        Idle,
        Wander,
        RWander,
        RSwing,
        Stun,
        Notice,
        Follow,
        Transform,
        RAttack,
        RShortAttack,
        Despawn
    }

    public Renderer[] renderers;
    public Transform _bulletPosition;
    public Transform feetTransform;
    public GameObject bulletPrefab;

    [Header("Sounds")] public Sound soundHit;

    public Sound genericLines;
    public Sound SadLines;
    public Sound EnvyLines;
    public Sound HateLines;
    public Sound CuteLines;
    public Sound MurderLines;
    public Sound RageLines;

    public AnimationCurve shootLineWidthCurve;
    public ParticleSystem minigunParticles;
    public bool Terminator;
    public bool Transformed;

    [Header("State")] [SerializeField] public State CurrentState;

    [SerializeField] public float stateTimer;
    [SerializeField] public float stateHaltTimer;

    [Header("Animation")] [SerializeField] private AnimationCurve hurtCurve;

    [SerializeField] private SkinnedMeshRenderer[] _skinnedMeshRenderer;
    [SerializeField] private MeshRenderer[] _MeshRenderer;

    [Header("Rotation and LookAt")] public SpringQuaternion horizontalRotationSpring;

    public LostDroidAnimationController LostDroidAnim;
    private readonly List<Material> _hurtMaterial = new();
    private readonly Color[] colors = new Color[5];

    private Vector3 _agentDestination;
    internal float _animSpeed = 1f;
    private float _attackCooldown;
    internal bool _attackImpulse;
    internal bool _attackShortImpulse;
    private float _avoidDist;
    private float _bulletFireCooldown;
    private float _bulletSpread;
    internal int _damageAmount;
    internal bool _deathImpulse;
    internal bool _despawnImpulse;
    internal Enemy _enemy;
    internal bool _fireBullets;
    internal bool _firstStun;
    internal bool _hasSpawned;
    private Quaternion _horizontalRotationTarget = Quaternion.identity;
    private int _hurtAmount;
    private bool _hurtImpulse;
    internal bool _isSprinting;
    internal bool _isStun;
    internal bool _isTurning;
    internal bool _isTurningToPlayer;
    internal bool _isWalking;
    private float _overrideAgentLerp;
    private PhotonView _photonView;
    private bool _stateImpulse;
    internal bool _swingImpulse;
    private bool _talker;
    private float _talkTimer;
    private PlayerAvatar _targetPlayer;
    private Vector3 _targetPosition;
    internal float _transformCount;
    internal float _transformCountMax;
    internal bool _transformImpulse;
    private Vector3 _turnPosition;
    private int droidVariant;
    private float hurtLerp;

    private EnemyNavMeshAgent _navMeshAgent => _enemy.NavMeshAgent;
    private EnemyRigidbody _rigidbody => _enemy.Rigidbody;
    private EnemyParent _enemyParent => _enemy.EnemyParent;
    private EnemyVision _Vision => _enemy.Vision;
    public Enemy Enemy => _enemy;

    private void Awake()
    {
        _enemy = GetComponent<Enemy>();
        _photonView = GetComponent<PhotonView>();
        _hurtAmount = Shader.PropertyToID("_ColorOverlayAmount");

        colors[0] = new Color(1f, 0.1764706f, 0.1764706f, 0f);
        colors[1] = new Color(0.4117647f, 0.466666669f, 1f, 0f);
        colors[2] = new Color(1f, 0.7607843f, 0f, 0f);
        colors[3] = new Color(0.956862748f, 0.08627451f, 0.784313738f, 0f);
        colors[4] = new Color(1f, 1f, 1f, 0f);

        if (Terminator)
        {
            _transformCount = 5f;
            _transformCountMax = Random.Range(5f, 10f);
            _bulletSpread = 10f;
        }
        else
        {
            _transformCount = 8f;
            _transformCountMax = Random.Range(6f, 12f);
            _bulletSpread = 20f;
        }

        _talker = Random.value < 0.5;
        _talkTimer = Random.Range(5f, 28f);
        _avoidDist = Random.Range(2f, 4f);

        foreach (var skinnedMeshRenderer in _skinnedMeshRenderer)
        {
            if (skinnedMeshRenderer != null)
            {
                _hurtMaterial.AddRange(skinnedMeshRenderer.materials);
            }
        }

        foreach (var renderer in _MeshRenderer)
        {
            if (renderer != null)
            {
                _hurtMaterial.AddRange(renderer.materials);
            }
        }

        hurtCurve = AssetManager.instance.animationCurveImpact;
    }

    private void Update()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !LevelGenerator.Instance.Generated)
        {
            return;
        }

        if (!_enemy.IsStunned())
        {
            if (_enemy.EnemyParent.Enemy.CurrentState == (EnemyState)11 && CurrentState != State.Despawn)
            {
                UpdateState(State.Despawn);
            }

            switch (CurrentState)
            {
                case State.Spawn:
                    StateSpawn();
                    break;
                case State.Idle:
                    StateIdle();
                    break;
                case State.Wander:
                    StateWander();
                    break;
                case State.RWander:
                    StateWander();
                    break;
                case State.RSwing:
                    StateRSwing();
                    break;
                case State.Stun:
                    StateStun();
                    break;
                case State.Notice:
                    StateNotice();
                    break;
                case State.Follow:
                    StateFollow();
                    break;
                case State.Transform:
                    StateTransform();
                    break;
                case State.RAttack:
                    StateRAttack();
                    break;
                case State.RShortAttack:
                    StateRShortAttack();
                    break;
                case State.Despawn:
                    StateDespawn();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            RotationLogic();
        }
        else
        {
            UpdateState(State.Stun);
            StateStun();
        }

        HurtEffect();

        if (_transformCount < _transformCountMax)
        {
            _transformCount += Time.deltaTime * 0.25f;
        }

        if (_attackCooldown > 0f)
        {
            _attackCooldown -= Time.deltaTime;
        }

        if (_talkTimer > 0f)
        {
            _talkTimer -= Time.deltaTime;
        }
        else
        {
            Talk();
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                _talkTimer = Random.Range(5f, 28f);
            }
        }
    }

    public void FireBullets()
    {
        if (!_fireBullets)
        {
            return;
        }

        if (_bulletFireCooldown <= 0f)
        {
            _bulletFireCooldown = 0.02f;
            FireBulletRPC();
        }

        _bulletFireCooldown -= Time.deltaTime;
    }

    public bool isPlayerNear(Transform baseTransform)
    {
        using var enumerator = GameDirector.instance.PlayerList.GetEnumerator();
        if (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            return !current.spectating && Vector3.Distance(current.transform.position, baseTransform.position) < 32f;
        }

        return false;
    }

    public void OnDeath()
    {
        _deathImpulse = true;
        _enemy.Stunned = false;
        LostDroidAnim.PlayDeathParticles();

        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            return;
        }

        _enemy.EnemyParent.Despawn();
    }

    public void OnDestroyerVision()
    {
        if (CurrentState != State.RWander)
        {
            return;
        }

        _targetPlayer = _enemy.StateInvestigate.Player.playerAvatarScript;
        if (_targetPlayer.isCrawling || _targetPlayer.isCrouching)
        {
            return;
        }

        if (Vector3.Distance(_targetPlayer.transform.position, transform.position) < 2f)
        {
            UpdateState(State.RSwing);
        }
        else
        {
            UpdateState(State.RShortAttack);
        }

        if (GameManager.Multiplayer())
        {
            _photonView.RPC("TargetPlayerRPC", RpcTarget.All, _targetPlayer.photonView.ViewID);
        }
    }

    public void OnGrab()
    {
        _targetPlayer = _enemy.Vision.onVisionTriggeredPlayer;

        if (CurrentState == State.Idle || CurrentState == State.Wander)
        {
            if (GameManager.Multiplayer() && _targetPlayer != null)
            {
                _photonView.RPC("TargetPlayerRPC", RpcTarget.All, _targetPlayer.photonView.ViewID);
            }

            if (_targetPlayer == null)
            {
                return;
            }

            UpdateState(State.Notice);
        }
        else if (CurrentState == State.RWander)
        {
            if (GameManager.Multiplayer() && _targetPlayer != null)
            {
                _photonView.RPC("TargetPlayerRPC", RpcTarget.All, _targetPlayer.photonView.ViewID);
            }

            UpdateState(State.RSwing);
        }
    }

    public void OnHurt()
    {
        if (Transformed || !isPlayerNear(transform))
        {
            return;
        }

        if (_damageAmount < 2)
        {
            ++_damageAmount;
        }
        else
        {
            UpdateState(State.Transform);
        }
    }

    public void OnSpawn()
    {
        if (SemiFunc.IsMasterClientOrSingleplayer() && SemiFunc.EnemySpawn(_enemy))
        {
            UpdateState(State.Spawn);
        }

        _damageAmount = 0;
        _talker = Random.value < 0.5;
        _talkTimer = Random.Range(5f, 28f);
        _avoidDist = Random.Range(2f, 4f);

        if (GameManager.Multiplayer())
        {
            _photonView.RPC("IsStunnedRPC", RpcTarget.All, false);
        }
        else
        {
            IsStunnedRPC(false);
        }

        if (Terminator)
        {
            return;
        }

        var num = Random.Range(0, colors.Length);
        if (GameManager.Multiplayer() && SemiFunc.IsMasterClientOrSingleplayer())
        {
            _photonView.RPC("PickRandomVariantRPC", RpcTarget.All, num);
        }
        else
        {
            PickRandomVariantRPC(num);
        }
    }

    public void OnVision()
    {
        if (!Transformed)
        {
            if (CurrentState == State.Wander || CurrentState == State.Idle)
            {
                _targetPlayer = _enemy.Vision.onVisionTriggeredPlayer;
                if (_targetPlayer.isCrawling || _targetPlayer.isCrouching)
                {
                    return;
                }

                UpdateState(State.Notice);

                if (GameManager.Multiplayer())
                {
                    _photonView.RPC("TargetPlayerRPC", RpcTarget.All, _targetPlayer.photonView.ViewID);
                }
            }
            else if ((CurrentState == State.Follow || CurrentState == State.Notice) &&
                     _targetPlayer == _enemy.Vision.onVisionTriggeredPlayer &&
                     !_targetPlayer.isCrawling && !_targetPlayer.isCrouching)
            {
                stateTimer = MathF.Max(stateTimer, 6f);
            }
        }
        else
        {
            if (CurrentState != State.RWander)
            {
                return;
            }

            _targetPlayer = _enemy.Vision.onVisionTriggeredPlayer;
            if (_targetPlayer.isCrawling)
            {
                return;
            }

            if (Vector3.Distance(_targetPlayer.transform.position, transform.position) < 3f)
            {
                UpdateState(State.RSwing);
            }
            else if (_attackCooldown > 0f)
            {
                UpdateState(State.RShortAttack);
            }
            else
            {
                UpdateState(State.RAttack);
            }

            if (GameManager.Multiplayer())
            {
                _photonView.RPC("TargetPlayerRPC", RpcTarget.All, _targetPlayer.photonView.ViewID);
            }
        }
    }

    [PunRPC]
    public void ShootBulletRPC(Vector3 _endPosition, bool _hit)
    {
        var component = Instantiate(bulletPrefab, _bulletPosition.position, _bulletPosition.rotation)
            .GetComponent<ItemGunBullet>();
        component.hitPosition = _endPosition;
        component.bulletHit = _hit;
        soundHit.Play(_endPosition);
        component.shootLineWidthCurve = shootLineWidthCurve;
        component.ActivateAll();
    }

    [PunRPC]
    private void AttackImpulseRPC()
    {
        _attackImpulse = true;
    }

    [PunRPC]
    private void AttackShortImpulseRPC()
    {
        _attackShortImpulse = true;
    }

    [PunRPC]
    private void ChangeAnimSpeedRPC(float value)
    {
        _animSpeed = value;
    }

    private bool CheckPathCompletion()
    {
        return _navMeshAgent.Agent.remainingDistance <= 0.07f;
    }

    [PunRPC]
    private void DespawnImpulseRPC()
    {
        _despawnImpulse = true;
    }

    [PunRPC]
    private void FireBulletRPC()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            return;
        }

        var _endPosition = _bulletPosition.position;
        var _hit = false;
        var flag = false;
        var forward = _bulletPosition.forward;

        var num1 = Random.Range(0f, _bulletSpread);
        var num2 = Random.Range(0f, 360f);
        var normalized = Vector3.Cross(forward, Random.onUnitSphere).normalized;
        var quaternion = Quaternion.AngleAxis(num1, normalized);
        var direction = (Quaternion.AngleAxis(num2, forward) * quaternion * forward).normalized;

        if (Physics.Raycast(_bulletPosition.position, direction, out var raycastHit, 80f,
                SemiFunc.LayerMaskGetVisionObstruct() + LayerMask.GetMask("enemy")))
        {
            _endPosition = raycastHit.point;
            _hit = true;
        }
        else
        {
            flag = true;
        }

        if (flag)
        {
            _endPosition = _bulletPosition.position + _bulletPosition.forward * 80f;
        }

        ShootBullet(_endPosition, _hit);
    }

    private void HurtEffect()
    {
        if (!_hurtImpulse)
        {
            return;
        }

        hurtLerp += 2.5f * Time.deltaTime;
        hurtLerp = Mathf.Clamp01(hurtLerp);

        foreach (var material in _hurtMaterial)
        {
            if (material != null)
            {
                material.SetFloat(_hurtAmount, hurtCurve.Evaluate(hurtLerp));
            }

            if (hurtLerp >= 1f)
            {
                hurtLerp = 0f;
                _hurtImpulse = false;
                if (material != null)
                {
                    material.SetFloat(_hurtAmount, 0f);
                }
            }
        }
    }

    [PunRPC]
    private void IsSprintingRPC(bool value)
    {
        _isSprinting = value;
    }

    [PunRPC]
    private void IsStunnedRPC(bool value)
    {
        _isStun = value;
    }

    [PunRPC]
    private void IsTurningRPC(bool value)
    {
        _isTurning = value;
    }

    [PunRPC]
    private void IsWalkingRPC(bool value)
    {
        _isWalking = value;
    }

    [PunRPC]
    private void PickRandomVariantRPC(int value)
    {
        droidVariant = value;
        foreach (var renderer in renderers)
        {
            renderer.material.SetColor("_AlbedoColor", colors[droidVariant]);
        }
    }

    private void PlayVoicelineMultiplayer(int value)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            return;
        }

        if (GameManager.Multiplayer())
        {
            _photonView.RPC("PlayVoicelineRPC", RpcTarget.All, value);
        }
        else
        {
            PlayVoicelineRPC(value);
        }
    }

    [PunRPC]
    private void PlayVoicelineRPC(int value)
    {
        switch (value)
        {
            case 0:
                HateLines.Play(genericLines.Source.transform.position);
                break;
            case 1:
                SadLines.Play(genericLines.Source.transform.position);
                break;
            case 2:
                EnvyLines.Play(genericLines.Source.transform.position);
                break;
            case 3:
                CuteLines.Play(genericLines.Source.transform.position);
                break;
            case 4:
                genericLines.Play(genericLines.Source.transform.position);
                break;
            case 5:
                RageLines.Play(genericLines.Source.transform.position);
                break;
            case 6:
                MurderLines.Play(genericLines.Source.transform.position);
                break;
        }
    }

    private void RotationLogic()
    {
        if (CurrentState != State.Stun && CurrentState != State.Idle && CurrentState != State.Notice &&
            CurrentState != State.Transform && CurrentState != State.RAttack && CurrentState != State.RShortAttack &&
            CurrentState != State.RSwing)
        {
            horizontalRotationSpring.speed = 10f;
            horizontalRotationSpring.damping = 1f;
            var normalized = _navMeshAgent.AgentVelocity.normalized;

            if (normalized.magnitude > 0.1f)
            {
                _horizontalRotationTarget = Quaternion.LookRotation(_navMeshAgent.AgentVelocity.normalized);
                _horizontalRotationTarget.eulerAngles = new Vector3(0f, _horizontalRotationTarget.eulerAngles.y, 0f);
            }
            else if (_isTurningToPlayer)
            {
                _horizontalRotationTarget = Quaternion.LookRotation(
                    _targetPlayer.transform.position - transform.position, Vector3.up);
                _horizontalRotationTarget.eulerAngles = new Vector3(0f, _horizontalRotationTarget.eulerAngles.y, 0f);
                TurnDroid();
            }
        }
        else if (CurrentState == State.Notice)
        {
            horizontalRotationSpring.speed = 10f;
            horizontalRotationSpring.damping = 1f;
            var direction = _targetPlayer.transform.position - transform.position;

            if (direction != Vector3.zero)
            {
                _horizontalRotationTarget = Quaternion.LookRotation(direction, Vector3.up);
                _horizontalRotationTarget.eulerAngles = new Vector3(0f, _horizontalRotationTarget.eulerAngles.y, 0f);
            }

            TurnDroid();
        }
        else if (CurrentState == State.RSwing || CurrentState == State.RShortAttack ||
                 (CurrentState == State.RAttack && _isTurningToPlayer))
        {
            horizontalRotationSpring.speed = 5f;
            horizontalRotationSpring.damping = 0.5f;
            var direction = _turnPosition - transform.position;

            if (direction != Vector3.zero)
            {
                _horizontalRotationTarget = Quaternion.LookRotation(direction, Vector3.up);
                _horizontalRotationTarget.eulerAngles = new Vector3(0f, _horizontalRotationTarget.eulerAngles.y, 0f);
            }
        }

        transform.rotation = SemiFunc.SpringQuaternionGet(horizontalRotationSpring, _horizontalRotationTarget);
    }

    private void ShootBullet(Vector3 _endPosition, bool _hit)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            return;
        }

        minigunParticles.Play();

        if (SemiFunc.IsMultiplayer())
        {
            _photonView.RPC("ShootBulletRPC", RpcTarget.All, _endPosition, _hit);
        }
        else
        {
            ShootBulletRPC(_endPosition, _hit);
        }
    }

    private void StateDespawn()
    {
        if (!_stateImpulse)
        {
            return;
        }

        _stateImpulse = false;
        _enemy.NavMeshAgent.Warp(feetTransform.position);
        _enemy.NavMeshAgent.ResetPath();

        if (GameManager.Multiplayer())
        {
            _photonView.RPC("DespawnImpulseRPC", RpcTarget.All);
        }
        else
        {
            DespawnImpulseRPC();
        }
    }

    private void StateFollow()
    {
        if (_stateImpulse)
        {
            stateTimer = 6f;
            _stateImpulse = false;

            if (GameManager.Multiplayer())
            {
                _photonView.RPC("IsSprintingRPC", RpcTarget.All, true);
                _photonView.RPC("IsTurningRPC", RpcTarget.All, false);
            }
            else
            {
                IsSprintingRPC(true);
                IsTurningRPC(false);
            }
        }
        else
        {
            if (Vector3.Distance(transform.position, _targetPlayer.transform.position) > _avoidDist)
            {
                _overrideAgentLerp -= Time.deltaTime / 0.01f;
                _enemy.Rigidbody.OverrideFollowPosition(0.2f, 5f, 30f);
                _overrideAgentLerp = Mathf.Clamp(_overrideAgentLerp, 0f, 1f);

                var num1 = 25f;
                var num2 = 25f;
                var num3 = Mathf.Lerp(_enemy.NavMeshAgent.DefaultSpeed, num1, _overrideAgentLerp);
                var num4 = Mathf.Lerp(_enemy.Rigidbody.positionSpeedChase, num2, _overrideAgentLerp);

                _enemy.NavMeshAgent.OverrideAgent(num3 * 2f, _enemy.NavMeshAgent.DefaultAcceleration, 0.2f);
                _enemy.Rigidbody.OverrideFollowPosition(1f, num4 * 2f);
                _targetPosition = _targetPlayer.transform.position;
                _enemy.NavMeshAgent.SetDestination(_targetPosition);
                _isTurningToPlayer = false;

                if (GameManager.Multiplayer())
                {
                    _photonView.RPC("IsSprintingRPC", RpcTarget.All, true);
                    _photonView.RPC("IsTurningRPC", RpcTarget.All, false);
                }
                else
                {
                    IsSprintingRPC(true);
                    IsTurningRPC(false);
                }
            }
            else
            {
                _navMeshAgent.Agent.ResetPath();
                _isTurningToPlayer = true;

                if (GameManager.Multiplayer())
                {
                    _photonView.RPC("IsSprintingRPC", RpcTarget.All, false);
                    _photonView.RPC("IsWalkingRPC", RpcTarget.All, false);
                }
                else
                {
                    IsSprintingRPC(false);
                    IsWalkingRPC(false);
                }
            }

            if (_transformCount > 0f)
            {
                _transformCount -= Time.deltaTime * 0.8f;
                stateTimer -= Time.deltaTime;

                if (stateTimer <= 0f)
                {
                    UpdateState(State.Wander);
                }
            }
            else if (isPlayerNear(transform))
            {
                UpdateState(State.Transform);
            }
            else
            {
                UpdateState(State.Wander);
            }
        }
    }

    private void StateIdle()
    {
        if (_stateImpulse)
        {
            _stateImpulse = false;
            _isTurningToPlayer = false;
            _navMeshAgent.Warp(transform.position);
            _navMeshAgent.ResetPath();
            stateTimer = Random.Range(6f, 8f);

            if (GameManager.Multiplayer())
            {
                _photonView.RPC("IsSprintingRPC", RpcTarget.All, false);
                _photonView.RPC("IsWalkingRPC", RpcTarget.All, false);
                _photonView.RPC("IsTurningRPC", RpcTarget.All, false);
            }
            else
            {
                IsSprintingRPC(false);
                IsWalkingRPC(false);
                IsTurningRPC(false);
            }
        }
        else
        {
            stateTimer -= Time.deltaTime;

            if (stateTimer <= 0f)
            {
                UpdateState(State.Wander);
            }
        }
    }

    private void StateNotice()
    {
        if (_stateImpulse)
        {
            _stateImpulse = false;
            _navMeshAgent.Warp(transform.position);
            _navMeshAgent.ResetPath();
            stateTimer = Random.Range(2f, 4f);

            if (GameManager.Multiplayer())
            {
                _photonView.RPC("IsSprintingRPC", RpcTarget.All, false);
                _photonView.RPC("IsWalkingRPC", RpcTarget.All, false);
            }
            else
            {
                IsSprintingRPC(false);
                IsWalkingRPC(false);
            }
        }
        else
        {
            if (_transformCount > 0f)
            {
                _transformCount -= Time.deltaTime * 0.4f;
                stateTimer -= Time.deltaTime;

                if (stateTimer <= 0f)
                {
                    UpdateState(State.Idle);
                }
            }
            else if (isPlayerNear(transform))
            {
                UpdateState(State.Transform);
            }
            else
            {
                UpdateState(State.Wander);
            }

            stateTimer -= Time.deltaTime;

            if (stateTimer <= 0f)
            {
                UpdateState(State.Follow);
            }
        }
    }

    private void StateRAttack()
    {
        if (_stateImpulse)
        {
            if (Terminator)
            {
                stateTimer = 17f;

                if (GameManager.Multiplayer())
                {
                    _photonView.RPC("ChangeAnimSpeedRPC", RpcTarget.All, 1f);
                }
                else
                {
                    ChangeAnimSpeedRPC(1f);
                }
            }
            else
            {
                stateTimer = 19.4f;
            }

            _attackCooldown = Random.Range(22f, 32f);
            _stateImpulse = false;
            _navMeshAgent.Warp(transform.position);
            _navMeshAgent.ResetPath();
            _turnPosition = transform.position;

            if (_targetPlayer != null)
            {
                _turnPosition = _targetPlayer.transform.position;
            }

            if (GameManager.Multiplayer())
            {
                _photonView.RPC("AttackImpulseRPC", RpcTarget.All);
                _photonView.RPC("IsWalkingRPC", RpcTarget.All, false);
            }
            else
            {
                AttackImpulseRPC();
                IsWalkingRPC(false);
            }
        }
        else
        {
            FireBullets();
            stateTimer -= Time.deltaTime;

            if (stateTimer <= 0f)
            {
                UpdateState(State.RWander);
            }
        }
    }

    private void StateRShortAttack()
    {
        if (_stateImpulse)
        {
            _navMeshAgent.Warp(transform.position);
            _navMeshAgent.ResetPath();
            _stateImpulse = false;
            stateTimer = 5.5f;

            if (_targetPlayer != null)
            {
                _turnPosition = _targetPlayer.transform.position;
            }

            if (Terminator)
            {
                if (GameManager.Multiplayer())
                {
                    _photonView.RPC("ChangeAnimSpeedRPC", RpcTarget.All, 1f);
                }
                else
                {
                    ChangeAnimSpeedRPC(1f);
                }
            }

            if (GameManager.Multiplayer())
            {
                _photonView.RPC("AttackShortImpulseRPC", RpcTarget.All);
                _photonView.RPC("IsWalkingRPC", RpcTarget.All, false);
            }
            else
            {
                AttackShortImpulseRPC();
                IsWalkingRPC(false);
            }
        }
        else
        {
            FireBullets();
            stateTimer -= Time.deltaTime;

            if (stateTimer <= 0f)
            {
                UpdateState(State.RWander);
            }
        }
    }

    private void StateRSwing()
    {
        if (_stateImpulse)
        {
            if (Terminator)
            {
                if (GameManager.Multiplayer())
                {
                    _photonView.RPC("ChangeAnimSpeedRPC", RpcTarget.All, 2f);
                }
                else
                {
                    ChangeAnimSpeedRPC(2f);
                }

                stateTimer = 1.2f;
            }
            else
            {
                stateTimer = 2.4f;
            }

            _stateImpulse = false;
            _navMeshAgent.Warp(transform.position);
            _navMeshAgent.ResetPath();
            _turnPosition = _targetPlayer.transform.position;

            if (GameManager.Multiplayer())
            {
                _photonView.RPC("SwingImpulseRPC", RpcTarget.All);
                _photonView.RPC("IsWalkingRPC", RpcTarget.All, false);
            }
            else
            {
                SwingImpulseRPC();
                IsWalkingRPC(false);
            }
        }
        else
        {
            stateTimer -= Time.deltaTime;

            if (stateTimer <= 0f)
            {
                if (Vector3.Distance(_targetPlayer.transform.position, transform.position) < 1f)
                {
                    UpdateState(State.RSwing);
                    _stateImpulse = true;
                }
                else
                {
                    UpdateState(State.RWander);
                }
            }
        }
    }

    private void StateSpawn()
    {
        if (_stateImpulse)
        {
            _stateImpulse = false;
            _navMeshAgent.Warp(transform.position);
            _navMeshAgent.ResetPath();

            if (GameManager.Multiplayer())
            {
                _photonView.RPC("ChangeAnimSpeedRPC", RpcTarget.All, 1f);
                _photonView.RPC("IsSprintingRPC", RpcTarget.All, false);
                _photonView.RPC("IsWalkingRPC", RpcTarget.All, false);
                _photonView.RPC("IsTurningRPC", RpcTarget.All, false);
            }
            else
            {
                ChangeAnimSpeedRPC(1f);
                IsSprintingRPC(false);
                IsWalkingRPC(false);
                IsTurningRPC(false);
            }

            stateTimer = 2f;
        }
        else
        {
            UpdateState(State.Wander);
            _hasSpawned = true;
        }
    }

    private void StateStun()
    {
        if (_stateImpulse)
        {
            _isTurningToPlayer = false;

            if (Terminator)
            {
                if (GameManager.Multiplayer())
                {
                    _photonView.RPC("ChangeAnimSpeedRPC", RpcTarget.All, 1f);
                }
                else
                {
                    ChangeAnimSpeedRPC(1f);
                }
            }

            _stateImpulse = false;

            if (!Transformed)
            {
                if (!_firstStun && _hasSpawned)
                {
                    if (GameManager.Multiplayer())
                    {
                        _photonView.RPC("IsStunnedRPC", RpcTarget.All, true);
                    }
                    else
                    {
                        IsStunnedRPC(true);
                    }

                    _firstStun = true;
                }
            }
            else
            {
                if (GameManager.Multiplayer())
                {
                    _photonView.RPC("IsStunnedRPC", RpcTarget.All, true);
                }
                else
                {
                    IsStunnedRPC(true);
                }
            }
        }

        if (_enemy.IsStunned())
        {
            return;
        }

        if (GameManager.Multiplayer())
        {
            _photonView.RPC("IsStunnedRPC", RpcTarget.All, false);
        }
        else
        {
            IsStunnedRPC(false);
        }

        if (Transformed)
        {
            if (_attackCooldown > 0f)
            {
                UpdateState(State.RShortAttack);
            }
            else
            {
                UpdateState(State.RAttack);
            }
        }
        else if (isPlayerNear(transform))
        {
            UpdateState(State.Transform);
        }
        else
        {
            UpdateState(State.Idle);
        }
    }

    private void StateTransform()
    {
        if (_stateImpulse)
        {
            stateTimer = 12f;
            _stateImpulse = false;
            _isTurningToPlayer = false;
            _navMeshAgent.Warp(transform.position);
            _navMeshAgent.ResetPath();

            if (GameManager.Multiplayer())
            {
                _photonView.RPC("TransformImpulseRPC", RpcTarget.All);
                _photonView.RPC("IsSprintingRPC", RpcTarget.All, false);
                _photonView.RPC("IsWalkingRPC", RpcTarget.All, false);
                _photonView.RPC("IsTurningRPC", RpcTarget.All, false);
            }
            else
            {
                TransformImpulseRPC();
                IsSprintingRPC(false);
                IsWalkingRPC(false);
                IsTurningRPC(false);
            }
        }
        else
        {
            stateTimer -= Time.deltaTime;

            if (stateTimer <= 0f)
            {
                UpdateState(State.RAttack);
                Transformed = true;
            }
        }
    }

    private void StateWander()
    {
        if (_stateImpulse)
        {
            var flag = false;
            var levelPoint = SemiFunc.LevelPointGet(transform.position, 10f, 20f);
            stateTimer = !Transformed ? 18f : 30f;

            if (levelPoint == null)
            {
                levelPoint = SemiFunc.LevelPointGet(transform.position, 5f, 999f);
            }

            int num;
            if (levelPoint != null && NavMesh.SamplePosition(
                    levelPoint.transform.position + Random.insideUnitSphere * 4f, out var navMeshHit, 8f, -1))
            {
                num = Physics.Raycast(navMeshHit.position, Vector3.down, 5f, LayerMask.GetMask("Default")) ? 1 : 0;

                if (num != 0)
                {
                    _agentDestination = navMeshHit.position;
                    flag = true;
                }
            }

            if (!flag)
            {
                return;
            }

            if (GameManager.Multiplayer())
            {
                _photonView.RPC("IsWalkingRPC", RpcTarget.All, true);
                _photonView.RPC("IsSprintingRPC", RpcTarget.All, false);
                _photonView.RPC("IsTurningRPC", RpcTarget.All, false);
            }
            else
            {
                IsWalkingRPC(true);
                IsTurningRPC(false);
                IsSprintingRPC(false);
            }

            _enemy.Rigidbody.notMovingTimer = 0f;
            _stateImpulse = false;

            if (Transformed)
            {
                if (Terminator)
                {
                    _navMeshAgent.Agent.speed = 0.75f;

                    if (GameManager.Multiplayer())
                    {
                        _photonView.RPC("ChangeAnimSpeedRPC", RpcTarget.All, 3f);
                    }
                    else
                    {
                        ChangeAnimSpeedRPC(3f);
                    }
                }
                else
                {
                    _navMeshAgent.Agent.speed = 0.25f;
                }
            }
            else
            {
                _navMeshAgent.Agent.speed = 1.4f;
            }

            _navMeshAgent.SetDestination(_agentDestination);
        }
        else
        {
            _navMeshAgent.SetDestination(_agentDestination);

            if (_enemy.Rigidbody.notMovingTimer > 2f)
            {
                stateTimer -= Time.deltaTime;
            }

            if (stateTimer <= 0f || CheckPathCompletion())
            {
                if (Transformed)
                {
                    UpdateState(State.RWander);
                    _stateImpulse = true;
                }
                else
                {
                    UpdateState(State.Idle);
                }
            }
        }
    }

    [PunRPC]
    private void SwingImpulseRPC()
    {
        _swingImpulse = true;
    }

    private void Talk()
    {
        if (!_talker)
        {
            return;
        }

        if (!Terminator && !Transformed && CurrentState != State.Transform)
        {
            if (_transformCount < _transformCountMax / 6f * 2f)
            {
                PlayVoicelineMultiplayer(5);
            }
            else if (Random.value < 0.95f)
            {
                switch (droidVariant)
                {
                    case 0:
                        PlayVoicelineMultiplayer(0);
                        break;
                    case 1:
                        PlayVoicelineMultiplayer(1);
                        break;
                    case 2:
                        PlayVoicelineMultiplayer(2);
                        break;
                    case 3:
                        PlayVoicelineMultiplayer(3);
                        break;
                    case 4:
                        PlayVoicelineMultiplayer(4);
                        break;
                }
            }
            else
            {
                PlayVoicelineMultiplayer(4);
            }
        }
        else
        {
            PlayVoicelineMultiplayer(6);
        }
    }

    [PunRPC]
    private void TargetPlayerRPC(int _playerID)
    {
        foreach (var player in GameDirector.instance.PlayerList)
        {
            if (player.photonView.ViewID == _playerID)
            {
                _targetPlayer = player;
            }
        }
    }

    [PunRPC]
    private void TransformImpulseRPC()
    {
        _transformImpulse = true;
    }

    private void TurnDroid()
    {
        var angle = Quaternion.Angle(transform.rotation, _horizontalRotationTarget);

        if (angle > 7f || angle < -7f)
        {
            if (GameManager.Multiplayer())
            {
                _photonView.RPC("IsTurningRPC", RpcTarget.All, true);
            }
            else
            {
                IsTurningRPC(true);
            }
        }
        else
        {
            if (GameManager.Multiplayer())
            {
                _photonView.RPC("IsTurningRPC", RpcTarget.All, false);
            }
            else
            {
                IsTurningRPC(false);
            }
        }
    }

    private void UpdateState(State _newState)
    {
        if (CurrentState == _newState)
        {
            return;
        }

        CurrentState = _newState;
        stateTimer = 0f;
        _stateImpulse = true;

        if (GameManager.Multiplayer())
        {
            _photonView.RPC("UpdateStateRPC", RpcTarget.All, CurrentState);
        }
        else
        {
            UpdateStateRPC(CurrentState);
        }
    }

    [PunRPC]
    private void UpdateStateRPC(State _state)
    {
        CurrentState = _state;

        if (CurrentState != State.Spawn)
        {
            return;
        }

        LostDroidAnim.SetSpawn();
    }
}