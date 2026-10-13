using System.Linq;
using Photon.Pun;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;
using VepMod.Patchs;
using VepMod.VepFramework;
using VepMod.VepFramework.Structures.FSM;
using Random = UnityEngine.Random;

// ReSharper disable InconsistentNaming

// ReSharper disable ClassCanBeSealed.Global
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

public class EnemyWhispral : StateMachineComponent<EnemyWhispral, EnemyWhispral.State>
{
    public enum State
    {
        Spawn = 0,
        Idle = 1,
        Roam = 2,
        Investigate = 3,
        NoticePlayer = 4,
        GoToPlayer = 5,
        PrepareAttach = 6,
        Attached = 7,
        Detach = 8,
        DetachWait = 9,
        Leave = 10,
        Stun = 11,
        StunEnd = 12,
        Despawn = 13
    }

    private static readonly VepLogger LOG = VepLogger.Create<EnemyWhispral>();

    [Header("Core refs")] public Enemy enemy;

    public EnemyWhispralAnim enemyWhispralAnim;

    [Header("Attach settings")]
    [Tooltip("Temps pendant lequel l’ennemi reste collé au joueur.")]
    public float attachedDuration = 200f;

    [Space] public SpringQuaternion rotationSpring;

    [HideInInspector] public Vector3 agentDestination;
    [HideInInspector] public PlayerAvatar? playerTarget;
    [HideInInspector] public Transform? attachAnchor;
    [HideInInspector] public float attachedTimer;

    private State _currentState;
    private float grabAggroTimer;

    private PhotonView photonView;
    private Quaternion rotationTarget;

    protected override State DefaultState => State.Spawn;

    public State CurrentState
    {
        get => SemiFunc.IsMasterClientOrSingleplayer() ? Fsm.CurrentStateStateId : _currentState;
        set
        {
            if (SemiFunc.IsMasterClientOrSingleplayer()) return;
            _currentState = value;
        }
    }


    protected override void Awake()
    {
        base.Awake();
        photonView = GetComponent<PhotonView>();
#if DEBUG
        var visualsCapsule = transform.parent?.Find("Visuals/Capsule");
        if (visualsCapsule != null)
        {
            visualsCapsule.gameObject.SetActive(true);
            LOG.Debug("Debug mode: Whispral visuals enabled");
        }
        else
        {
            LOG.Warning("Debug mode: Could not find Visuals/Capsule from parent");
        }
#endif

        Fsm.AddState(State.Spawn, new SpawnState());
        Fsm.AddState(State.Idle, new IdleState());
        Fsm.AddState(State.Roam, new RoamState());
        Fsm.AddState(State.Investigate, new InvestigateState());
        Fsm.AddState(State.NoticePlayer, new PlayerNoticeState());
        Fsm.AddState(State.GoToPlayer, new PlayerGoToState());
        Fsm.AddState(State.PrepareAttach, new AttachPrepareState());
        Fsm.AddState(State.Attached, new AttachedState());
        Fsm.AddState(State.Detach, new DetachState());
        Fsm.AddState(State.DetachWait, new DetachWaitState());
        Fsm.AddState(State.Leave, new LeaveState());
        Fsm.AddState(State.Stun, new StunState());
        Fsm.AddState(State.StunEnd, new StunEndState());
        Fsm.AddState(State.Despawn, new DespawnState());
    }

    protected override void Update()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) // Only the master handle the enemy logic
        {
            return;
        }

        UpdateGrabAggroTimer();

        // Transitions forced by the game's Enemy system
        CheckForStunState();
        CheckForDespawnState();
        RotationLogic();
        base.Update(); // fsm.Update() 
    }

    private void FixedUpdate()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            return;
        }

        AttachFollowLogic();
    }

    private void CheckForDespawnState()
    {
        var canDespawn = enemy.CurrentState == EnemyState.Despawn && !enemy.IsStunned();
        if (canDespawn)
        {
            Fsm.NextStateStateId = State.Despawn;
        }
    }

    private void CheckForStunState()
    {
        if (enemy.IsStunned())
        {
            Fsm.NextStateStateId = State.Stun;
        }
    }


    private void UpdateGrabAggroTimer()
    {
        // While positive, decrement the grab aggro timer each frame so the enemy won't immediately react after being grabbed.
        // The timer is set in OnGrabbed (e.g. 60f). Checking <0f avoids decrementing when already zero and prevents
        // negative values.
        if (grabAggroTimer > 0f)
        {
            grabAggroTimer -= Time.deltaTime;
            if (grabAggroTimer < 0f) grabAggroTimer = 0f;
        }
    }

    #region Rotation / Attach follow

    private void RotationLogic()
    {
        var hasPlayerTargetedState = CurrentState is State.NoticePlayer or State.GoToPlayer or State.PrepareAttach;
        if (hasPlayerTargetedState && playerTarget != null)
        {
            if (Vector3.Distance(playerTarget.transform.position, transform.position) > 0.1f)
            {
                var look = Quaternion.LookRotation(playerTarget.transform.position - transform.position);
                look.eulerAngles = new Vector3(0f, look.eulerAngles.y, 0f);
                rotationTarget = look;
            }
        }
        else if (enemy.NavMeshAgent.AgentVelocity.normalized.magnitude > 0.1f)
        {
            var look = Quaternion.LookRotation(enemy.NavMeshAgent.AgentVelocity.normalized);
            look.eulerAngles = new Vector3(0f, look.eulerAngles.y, 0f);
            rotationTarget = look;
        }

        transform.rotation = SemiFunc.SpringQuaternionGet(rotationSpring, rotationTarget);
    }

    private void AttachFollowLogic()
    {
        if (CurrentState != State.Attached) return;

        if (playerTarget == null || playerTarget.isDisabled || attachAnchor == null)
        {
            return;
        }

        var desiredPos = attachAnchor.position + attachAnchor.TransformDirection(new Vector3(0f, 0.3f, -0.3f));
        enemy.Rigidbody.transform.position = Vector3.Lerp(
            enemy.Rigidbody.transform.position,
            desiredPos,
            10f * Time.fixedDeltaTime);

        var desiredRot = Quaternion.LookRotation(attachAnchor.forward, Vector3.up);
        enemy.Rigidbody.transform.rotation = Quaternion.Slerp(
            enemy.Rigidbody.transform.rotation,
            desiredRot,
            10f * Time.fixedDeltaTime);
    }

    #endregion

    #region RPC

    [PunRPC]
    private void UpdateStateRPC(State state, PhotonMessageInfo _info = default)
    {
        var sentByMe = _info.Sender != null && _info.Sender.UserId == PhotonNetwork.LocalPlayer.UserId;
        // var otherSentByMe = _info.Sender is { IsLocal: true };
        if (SemiFunc.MasterOnlyRPC(_info))
        {
            if (sentByMe) return;
            CurrentState = state;
        }
    }

    [PunRPC]
    private void UpdatePlayerTargetRPC(int _photonViewID, PhotonMessageInfo _info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(_info))
        {
            return;
        }

        foreach (var item in SemiFunc.PlayerGetList())
        {
            if (item.photonView.ViewID == _photonViewID)
            {
                playerTarget = item;
                attachAnchor = SemiFunc.PlayerGetFaceEyeTransform(item);
                break;
            }
        }
    }

    [PunRPC]
    private void WhispralAttachmentRPC(int targetViewID, bool attach, PhotonMessageInfo _info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(_info))
        {
            return;
        }

        PlayerAvatar? targetPlayer = null;
        foreach (var player in GameDirector.instance.PlayerList)
        {
            if (player && player.photonView && player.photonView.ViewID == targetViewID)
            {
                targetPlayer = player;
                break;
            }
        }

        if (targetPlayer == null)
        {
            LOG.Warning($"WhispralAttachmentRPC: Player with ViewID {targetViewID} not found.");
            return;
        }

        LOG.Debug($"WhispralAttachmentRPC received: attach={attach} for player {targetPlayer.playerName}");
        var manager = targetPlayer.GetOrAddComponent<WhispralDebuffManager>();
        if (attach)
        {
            manager.RegisterAttachment();
        }
        else
        {
            manager.UnregisterAttachment();
        }
    }

    #endregion

    #region Enemy hooks

    public void OnSpawn()
    {
        if (SemiFunc.IsMasterClientOrSingleplayer() && SemiFunc.EnemySpawn(enemy))
        {
            Fsm.NextStateStateId = State.Spawn;
        }
    }

    public void OnHurt()
    {
        enemyWhispralAnim.Hurt();
    }

    public void OnDeath()
    {
        enemyWhispralAnim.Death();
        if (SemiFunc.IsMasterClientOrSingleplayer())
        {
            enemy.EnemyParent.Despawn();
        }
    }

    public void OnInvestigate()
    {
        var canInvestigate = CurrentState is State.Idle or State.Roam or State.Investigate;
        if (SemiFunc.IsMasterClientOrSingleplayer() && canInvestigate)
        {
            agentDestination = enemy.StateInvestigate.onInvestigateTriggeredPosition;
            Fsm.NextStateStateId = State.Investigate;
        }
    }

    public void OnVision()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            return;
        }

        var canNoticePlayer = CurrentState is State.Idle or State.Roam or State.Investigate or State.Leave;
        if (canNoticePlayer)
        {
            playerTarget = enemy.Vision.onVisionTriggeredPlayer;

            if (SemiFunc.IsMultiplayer())
            {
                photonView.RPC(nameof(UpdatePlayerTargetRPC), RpcTarget.All, playerTarget.photonView.ViewID);
            }

            Fsm.NextStateStateId = State.NoticePlayer;
        }
        else if (CurrentState == State.GoToPlayer)
        {
            Fsm.GetStateTyped<PlayerGoToState>(State.GoToPlayer)?.RefreshTimer();
        }
    }

    public void OnGrabbed()
    {
        // When Enemy is grabbed
        var canAggroPlayer = !(grabAggroTimer > 0f);
        if (SemiFunc.IsMasterClientOrSingleplayer() && canAggroPlayer && CurrentState == State.Leave)
        {
            grabAggroTimer = 60f;
            playerTarget = enemy.Rigidbody.onGrabbedPlayerAvatar;
            if (SemiFunc.IsMultiplayer())
            {
                photonView.RPC(nameof(UpdatePlayerTargetRPC), RpcTarget.All, playerTarget.photonView.ViewID);
            }

            Fsm.NextStateStateId = State.NoticePlayer;
        }
    }

    /// <summary>
    ///     E.g. when the Victim turns into an object.
    /// </summary>
    private void ForceDetach()
    {
        if (CurrentState is State.PrepareAttach or State.Attached)
        {
            Fsm.NextStateStateId = State.Detach;
        }
    }

    #endregion

    #region States

    private abstract class WhispralStateBase : StateMachineBase<StateMachine, State>.StateBase
    {
        protected StateMachine Fsm => Machine;
        protected EnemyWhispral Whispral => Fsm.Owner;
        protected Enemy Enemy => Whispral.enemy;
        protected EnemyWhispralAnim Anim => Whispral.enemyWhispralAnim;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            Enemy.Rigidbody.StuckReset();
            if (GameManager.Multiplayer())
            {
                Whispral.photonView.RPC(nameof(UpdateStateRPC), RpcTarget.All, Fsm.NextStateStateId);
            }
            else
            {
                Whispral.UpdateStateRPC(Fsm.NextStateStateId);
            }
        }

        /// <summary>
        ///     For leaving Attached or PrepareAttach without going through Detach.
        /// </summary>
        protected void CleanupAttachmentIfNeeded(State previous)
        {
            if (previous is not (State.Attached or State.PrepareAttach)) return;

            var player = Whispral.playerTarget;
            if (player == null) return;

            LOG.Debug($"Whispral interrupted while attached (-> {Fsm.CurrentStateStateId}), cleaning up debuffs.");
            if (SemiFunc.IsMultiplayer())
            {
                Whispral.photonView.RPC(nameof(WhispralAttachmentRPC), RpcTarget.All, player.photonView.ViewID, false);
            }
            else
            {
                var manager = player.GetComponent<WhispralDebuffManager>();
                if (manager)
                {
                    manager.UnregisterAttachment();
                }
            }

            Whispral.attachAnchor = null;
        }
    }

    private sealed class SpawnState : WhispralStateBase
    {
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = 1f;
        }

        public override void OnStateUpdate()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Fsm.NextStateStateId = State.Idle;
            }
        }
    }

    private sealed class IdleState : WhispralStateBase
    {
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = Random.Range(1f, 3f);
            Enemy.NavMeshAgent.Warp(Enemy.Rigidbody.transform.position);
            Enemy.NavMeshAgent.ResetPath();
        }

        public override void OnStateUpdate()
        {
            if (SemiFunc.EnemySpawnIdlePause())
            {
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Fsm.NextStateStateId = State.Roam;
            }

            if (SemiFunc.EnemyForceLeave(Enemy))
            {
                Fsm.NextStateStateId = State.Leave;
            }
        }
    }

    private sealed class RoamState : WhispralStateBase
    {
        private const float TimeToStopRoaming = 2f;
        private const float TimeToRoam = 5f;
        private bool _hasDestination;
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = TimeToRoam;
            _hasDestination = false;

            var levelPoint = SemiFunc.LevelPointGet(Whispral.transform.position, 10f, 25f)
                             ?? SemiFunc.LevelPointGet(Whispral.transform.position, 0f, 999f);

            if (levelPoint && SamplePosition(levelPoint, out var hit) && Raycast(hit)) // if valid position found
            {
                Enemy.NavMeshAgent.SetDestination(hit.position);
                Enemy.Rigidbody.notMovingTimer = 0f;
                _hasDestination = true;
            }
        }


        public override void OnStateUpdate()
        {
            if (SemiFunc.EnemyForceLeave(Enemy))
            {
                Fsm.NextStateStateId = State.Leave;
                return;
            }

            if (!_hasDestination)
            {
                Fsm.NextStateStateId = State.Idle;
                return;
            }

            SemiFunc.EnemyCartJump(Enemy);

            var canRoam = Enemy.Rigidbody.notMovingTimer > TimeToStopRoaming;
            if (canRoam)
            {
                _timer -= Time.deltaTime;
            }

            var shouldNotMove = _timer <= 0f || !Enemy.NavMeshAgent.HasPath();
            if (shouldNotMove)
            {
                SemiFunc.EnemyCartJumpReset(Enemy);
                Fsm.NextStateStateId = State.Idle;
            }
        }

        private static bool Raycast(NavMeshHit hit)
        {
            return Physics.Raycast(hit.position, Vector3.down, 5f, LayerMask.GetMask("Default"));
        }

        private static bool SamplePosition(LevelPoint levelPoint, out NavMeshHit hit)
        {
            return NavMesh.SamplePosition(levelPoint.transform.position + Random.insideUnitSphere * 3f, out hit, 5f,
                -1);
        }
    }

    private sealed class InvestigateState : WhispralStateBase
    {
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = 5f;
            Enemy.Rigidbody.notMovingTimer = 0f;
        }

        public override void OnStateUpdate()
        {
            if (SemiFunc.EnemyForceLeave(Enemy))
            {
                Fsm.NextStateStateId = State.Leave;
                return;
            }

            Enemy.NavMeshAgent.SetDestination(Whispral.agentDestination);
            SemiFunc.EnemyCartJump(Enemy);

            if (Enemy.Rigidbody.notMovingTimer > 2f)
            {
                _timer -= Time.deltaTime;
            }

            if (_timer <= 0f || !Enemy.NavMeshAgent.HasPath())
            {
                SemiFunc.EnemyCartJumpReset(Enemy);
                Fsm.NextStateStateId = State.Idle;
            }
        }
    }

    private sealed class PlayerNoticeState : WhispralStateBase
    {
        private const float TimeToNoticePlayer = 2f;
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            Enemy.NavMeshAgent.Warp(Enemy.Rigidbody.transform.position);
            Enemy.NavMeshAgent.ResetPath();
            _timer = TimeToNoticePlayer;
        }

        public override void OnStateUpdate()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Fsm.NextStateStateId = State.GoToPlayer;
            }
        }
    }

    private sealed class PlayerGoToState : WhispralStateBase
    {
        private const float TimeToStopGoingToPlayer = 2f;
        private const float AttachDistanceThreshold = 1.5f;

        private bool _agentSet;
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = TimeToStopGoingToPlayer;
            _agentSet = true;
        }

        public void RefreshTimer()
        {
            _timer = TimeToStopGoingToPlayer;
        }

        public override void OnStateUpdate()
        {
            _timer -= Time.deltaTime;

            var player = Whispral.playerTarget;
            if (player == null || player.isDisabled || _timer <= 0f)
            {
                Fsm.NextStateStateId = State.Leave;
                return;
            }

            var playerPos = player.transform.position;
            SemiFunc.EnemyCartJump(Enemy);

            if (Enemy.Jump.jumping)
            {
                Enemy.NavMeshAgent.Disable(0.5f);
                Whispral.transform.position = Vector3.MoveTowards(
                    Whispral.transform.position,
                    playerPos,
                    5f * Time.deltaTime);
                _agentSet = true;
            }
            else if (!Enemy.NavMeshAgent.IsDisabled())
            {
                var isStuck = !_agentSet &&
                              Enemy.NavMeshAgent.HasPath() &&
                              Vector3.Distance(Enemy.Rigidbody.transform.position + Vector3.down * 0.75f,
                                  Enemy.NavMeshAgent.GetDestination()) < 0.25f;
                if (isStuck)
                {
                    Enemy.Jump.StuckTrigger(Enemy.Rigidbody.transform.position - playerPos);
                }

                Enemy.NavMeshAgent.SetDestination(playerPos);
                Enemy.NavMeshAgent.OverrideAgent(5f, 10f, 0.25f);
                _agentSet = false;
            }

            if (Vector3.Distance(Enemy.Rigidbody.transform.position, playerPos) < AttachDistanceThreshold)
            {
                SemiFunc.EnemyCartJumpReset(Enemy);
                Fsm.NextStateStateId = State.PrepareAttach;
            }
        }
    }

    private sealed class AttachPrepareState : WhispralStateBase
    {
        private const float TimeToPrepareAttach = 1f;
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = TimeToPrepareAttach;
            Enemy.NavMeshAgent.Warp(Enemy.Rigidbody.transform.position);
            Enemy.NavMeshAgent.ResetPath();
        }

        public override void OnStateUpdate()
        {
            var player = Whispral.playerTarget;
            if (player == null || player.isDisabled)
            {
                Fsm.NextStateStateId = State.Leave;
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Fsm.NextStateStateId = State.Attached;
            }
        }
    }

    private sealed class AttachedState : WhispralStateBase
    {
        private const float VoiceFilterProbability = 0.5f;
        private float voiceTimer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            Whispral.attachedTimer = Whispral.attachedDuration;
            voiceTimer = GetRandomVoiceDelay();
            var player = Whispral.playerTarget;
            Whispral.attachAnchor = player ? SemiFunc.PlayerGetFaceEyeTransform(player) : null;
            StartAttachEffects();
        }

        public override void OnStateUpdate()
        {
            if (CheckLeaveState()) return;
            CheckDetachState();

            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            voiceTimer -= Time.deltaTime;
            if (!(voiceTimer <= 0f)) return;
            SendVoiceCommand();
            voiceTimer = GetRandomVoiceDelay();
        }

        private static float GetRandomVoiceDelay()
        {
            if (VepMod.VoiceDelay == null)
            {
                return Random.Range(3f, 8f);
            }

            return Random.Range(VepMod.VoiceDelay.Min, VepMod.VoiceDelay.Max);
        }

        private void SendVoiceCommand()
        {
            var targetPlayer = Whispral.playerTarget;
            if (targetPlayer == null) return;

            var localMimics = VepFinder.LocalMimics;
            if (!localMimics)
            {
                LOG.Warning("LocalMimics not found, cannot send voice command.");
                return;
            }

            var targetViewID = targetPlayer.photonView.ViewID;

            var sourcePlayerNickName = ChooseRandomSourcePlayer(targetPlayer);
            if (sourcePlayerNickName == null)
            {
                LOG.Warning("No source player available for voice command.");
                return;
            }

            var applyFilter = VepMod.ConfigVoiceFilterEnabled.Value && Random.value < VoiceFilterProbability;

            LOG.Debug(
                $"Sending voice command: target={targetPlayer.photonView.Owner.NickName}, source={sourcePlayerNickName}, filter={applyFilter}");
            localMimics.SendPlayVoiceCommand(targetViewID, sourcePlayerNickName, applyFilter);
        }

        private static string? ChooseRandomSourcePlayer(PlayerAvatar targetPlayer)
        {
            var targetNickName = targetPlayer.photonView.Owner?.NickName;

            var players = PhotonNetwork.PlayerList
                .Where(p => !string.IsNullOrEmpty(p.NickName) && p.NickName != targetNickName)
                .ToList();

            if (players.Count == 0) return null;
            return players[Random.Range(0, players.Count)].NickName;
        }

        private void StartAttachEffects()
        {
            var player = Whispral.playerTarget;
            if (player == null) return;

            LOG.Debug("Whispral attached to player, sending debuff RPC.");
            if (SemiFunc.IsMultiplayer())
            {
                Whispral.photonView.RPC(nameof(WhispralAttachmentRPC), RpcTarget.All, player.photonView.ViewID, true);
            }
            else
            {
                var manager = player.GetOrAddComponent<WhispralDebuffManager>();
                manager.RegisterAttachment();
            }
        }

        private void CheckDetachState()
        {
            Whispral.attachedTimer -= Time.deltaTime;
            var player = Whispral.playerTarget;
            if (Whispral.attachedTimer <= 0f || player == null || player.isDisabled || player.isTumbling)
            {
                Fsm.NextStateStateId = State.Detach;
            }
        }

        private bool CheckLeaveState()
        {
            var player = Whispral.playerTarget;
            if (player == null || player.isDisabled)
            {
                Fsm.NextStateStateId = State.Leave;
                return true;
            }

            return false;
        }
    }

    private class DetachState : WhispralStateBase
    {
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = 0.5f;

            Enemy.NavMeshAgent.Warp(Enemy.Rigidbody.transform.position);
            Enemy.NavMeshAgent.ResetPath();

            StopAttachEffects();
        }

        public override void OnStateUpdate()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Whispral.attachAnchor = null;
                Fsm.NextStateStateId = State.DetachWait;
            }
        }

        private void StopAttachEffects()
        {
            var player = Whispral.playerTarget;
            if (player == null) return;

            LOG.Debug("Whispral detached from player, sending debuff RPC.");
            if (SemiFunc.IsMultiplayer())
            {
                Whispral.photonView.RPC(nameof(WhispralAttachmentRPC), RpcTarget.All, player.photonView.ViewID, false);
            }
            else
            {
                var manager = player.GetComponent<WhispralDebuffManager>();
                if (manager)
                {
                    manager.UnregisterAttachment();
                }
            }
        }
    }

    private class DetachWaitState : WhispralStateBase
    {
        private const float TimeToLeaveAfterDetach = 2f;
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = TimeToLeaveAfterDetach;
        }

        public override void OnStateUpdate()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Fsm.NextStateStateId = State.Leave;
            }
        }
    }

    private class LeaveState : WhispralStateBase
    {
        private const float TimeToLeave = 5f;
        private bool _hasDestination;
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = TimeToLeave;
            _hasDestination = false;

            var levelPoint = SemiFunc.LevelPointGetPlayerDistance(Whispral.transform.position, 30f, 50f)
                             ?? SemiFunc.LevelPointGetFurthestFromPlayer(Whispral.transform.position, 5f);

            if (levelPoint &&
                NavMesh.SamplePosition(levelPoint.transform.position + Random.insideUnitSphere * 3f,
                    out var hit, 5f, -1) &&
                Physics.Raycast(hit.position, Vector3.down, 5f, LayerMask.GetMask("Default")))
            {
                Whispral.agentDestination = hit.position;
                _hasDestination = true;
            }

            SemiFunc.EnemyLeaveStart(Enemy);

            if (_hasDestination)
            {
                Enemy.EnemyParent.SpawnedTimerSet(1f);
            }
        }

        public override void OnStateUpdate()
        {
            if (!_hasDestination)
            {
                Fsm.NextStateStateId = State.Idle;
                return;
            }

            if (Enemy.Rigidbody.notMovingTimer > 2f)
            {
                _timer -= Time.deltaTime;
            }

            Enemy.NavMeshAgent.SetDestination(Whispral.agentDestination);
            Enemy.NavMeshAgent.OverrideAgent(5f, 10f, 0.25f);
            SemiFunc.EnemyCartJump(Enemy);

            if (Vector3.Distance(Whispral.transform.position, Whispral.agentDestination) < 1f || _timer <= 0f)
            {
                SemiFunc.EnemyCartJumpReset(Enemy);
                Fsm.NextStateStateId = State.Idle;
            }
        }
    }

    private class StunState : WhispralStateBase
    {
        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            CleanupAttachmentIfNeeded(previous);
        }

        public override void OnStateUpdate()
        {
            if (!Enemy.IsStunned())
            {
                Fsm.NextStateStateId = State.StunEnd;
            }
        }
    }

    private class StunEndState : WhispralStateBase
    {
        private const float TimeToRecoverStun = 1f;
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = TimeToRecoverStun;
        }

        public override void OnStateUpdate()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Fsm.NextStateStateId = State.Leave;
            }
        }
    }

    private class DespawnState : WhispralStateBase
    {
        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            CleanupAttachmentIfNeeded(previous);
            Enemy.EnemyParent.Despawn();
            Fsm.NextStateStateId = State.Spawn;
        }
    }

    #endregion
}
