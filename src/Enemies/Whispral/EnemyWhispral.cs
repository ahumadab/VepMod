using System.Linq;
using Photon.Pun;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;
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
    [Tooltip("How long the Whispral stays attached to its Victim, in seconds.")]
    public float attachedDuration = 240f;

    [Space] public SpringQuaternion rotationSpring;

    [HideInInspector] public Vector3 agentDestination;
    [HideInInspector] public PlayerAvatar? playerTarget;
    [HideInInspector] public Transform? attachAnchor;
    [HideInInspector] public float attachedTimer;

    private State _currentState;
    private float? _diedAt;
    private float _grabAggroTimer;
    private float _returnDelay;

    private PhotonView _photonView;
    private Quaternion _rotationTarget;

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
        _photonView = GetComponent<PhotonView>();
#if DEBUG
        var visualsCapsule = transform.parent != null ? transform.parent.Find("Visuals/Capsule") : null;
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
        RefillSpawnTimer();

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

    // Runs before EnemyParent's Logic coroutine, which despawns an enemy whose spawn timer has run out (ADR-0008).
    private void RefillSpawnTimer()
    {
        var parent = enemy.EnemyParent;
        if (Presence.RefilledSpawnTimer(parent.SpawnedTimer) is not { } refilled) return;

        LOG.Debug($"Whispral {_photonView.ViewID} spawn timer refilled: {parent.SpawnedTimer:F2} s -> {refilled:F0} s.");
        parent.SpawnedTimer = refilled;
    }

    /// <returns>The straight-line distance to the nearest alive player, in metres; infinity when none is alive.</returns>
    internal static float NearestPlayerDistance(Vector3 position)
    {
        var nearest = float.PositiveInfinity;
        foreach (var player in SemiFunc.PlayerGetList())
        {
            if (player.isDisabled) continue;
            nearest = Mathf.Min(nearest, Vector3.Distance(position, player.transform.position));
        }

        return nearest;
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
        if (_grabAggroTimer > 0f)
        {
            _grabAggroTimer -= Time.deltaTime;
            if (_grabAggroTimer < 0f) _grabAggroTimer = 0f;
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
                _rotationTarget = look;
            }
        }
        else if (enemy.NavMeshAgent.AgentVelocity.normalized.magnitude > 0.1f)
        {
            var look = Quaternion.LookRotation(enemy.NavMeshAgent.AgentVelocity.normalized);
            look.eulerAngles = new Vector3(0f, look.eulerAngles.y, 0f);
            _rotationTarget = look;
        }

        transform.rotation = SemiFunc.SpringQuaternionGet(rotationSpring, _rotationTarget);
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
        if (!SemiFunc.MasterOnlyRPC(_info)) return;

        // EnemyParent.Despawn deactivates this object on every client right after this RPC, before any Update: a
        // Whispral killed or despawned while attached would go, and come back, hidden.
        if (state == State.Despawn) enemyWhispralAnim.Despawn();
        if (sentByMe) return;
        CurrentState = state;
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
    private void AttachmentRPC(int victimViewID, int whispralViewID, bool attached, PhotonMessageInfo _info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(_info))
        {
            return;
        }

        var victim = SemiFunc.PlayerAvatarGetFromPhotonID(victimViewID);
        if (!victim)
        {
            // A Victim who left the game was no longer Haunted once their avatar was destroyed.
            LOG.Debug($"AttachmentRPC: no player with ViewID {victimViewID}.");
            return;
        }

        var haunting = victim.GetOrAddComponent<Haunting>();
        if (attached)
        {
            haunting.Attach(whispralViewID);
        }
        else
        {
            haunting.Detach(whispralViewID);
        }
    }

    /// <summary>Master only.</summary>
    internal void SendMimicCommand(PlayerAvatar victim, bool voiceFilter)
    {
        LOG.Debug($"Mimic command: Victim={victim.playerName}, voice filter={voiceFilter}");
        _photonView.RPC(nameof(MimicRPC), victim.photonView.Owner, voiceFilter);
    }

    // Sent to the Victim's client only (ADR-0002).
    [PunRPC]
    private void MimicRPC(bool voiceFilter, PhotonMessageInfo _info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(_info))
        {
            return;
        }

        var imitations = Imitations.Local;
        if (!imitations)
        {
            LOG.Debug("Mimic command: the local player is not a Victim, nothing plays.");
            return;
        }

        var speaker = imitations!.PickMimicSpeaker();
        if (speaker) speaker!.PlayMimic(voiceFilter);
    }

    private void ReportAttachment(int victimViewID, bool attached)
    {
        if (SemiFunc.IsMultiplayer())
        {
            _photonView.RPC(nameof(AttachmentRPC), RpcTarget.All, victimViewID, _photonView.ViewID, attached);
        }
        else
        {
            // Offline the game instantiates enemies without Photon, so every Whispral has ViewID 0.
            AttachmentRPC(victimViewID, GetInstanceID(), attached);
        }
    }

    #endregion

    #region Enemy hooks

    public void OnSpawn()
    {
        if (SemiFunc.IsMasterClientOrSingleplayer() && SemiFunc.EnemySpawn(enemy))
        {
            var sinceDeath = _diedAt is { } diedAt
                ? $", {Time.time - diedAt:F0} s after its death (return delay {_returnDelay:F0} s)"
                : "";
            LOG.Debug($"Whispral {_photonView.ViewID} spawns{sinceDeath}.");
            _diedAt = null;
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
            // Despawn deactivates this object at once: the FSM has to leave its state in this call, or a Whispral
            // killed while attached never ends its Attachment.
            LOG.Debug($"Whispral {_photonView.ViewID} killed in state {CurrentState}.");
            Fsm.NextStateStateId = State.Despawn;
            Fsm.Update();

            // Replaces the game's 480 to 500 s, its director multipliers and its x3 after a valuable drop (ADR-0008).
            var parent = enemy.EnemyParent;
            var gameDelay = parent.DespawnedTimer;
            _returnDelay = Presence.ReturnDelay(Random.value);
            parent.DespawnedTimerSet(_returnDelay);
            _diedAt = Time.time;
            LOG.Debug($"Whispral {_photonView.ViewID} returns in {parent.DespawnedTimer:F0} s " +
                      $"(drawn {_returnDelay:F0} s), not the game's {gameDelay:F0} s.");
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
                _photonView.RPC(nameof(UpdatePlayerTargetRPC), RpcTarget.All, playerTarget.photonView.ViewID);
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
        var canAggroPlayer = !(_grabAggroTimer > 0f);
        if (SemiFunc.IsMasterClientOrSingleplayer() && canAggroPlayer && CurrentState == State.Leave)
        {
            _grabAggroTimer = 60f;
            playerTarget = enemy.Rigidbody.onGrabbedPlayerAvatar;
            if (SemiFunc.IsMultiplayer())
            {
                _photonView.RPC(nameof(UpdatePlayerTargetRPC), RpcTarget.All, playerTarget.photonView.ViewID);
            }

            Fsm.NextStateStateId = State.NoticePlayer;
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
                Whispral._photonView.RPC(nameof(UpdateStateRPC), RpcTarget.All, Fsm.NextStateStateId);
            }
            else
            {
                Whispral.UpdateStateRPC(Fsm.NextStateStateId);
            }
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
        private bool _startPauseSkipReported;
        private float _timer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            _timer = Random.Range(1f, 3f);
            Enemy.NavMeshAgent.Warp(Enemy.Rigidbody.transform.position);
            Enemy.NavMeshAgent.ResetPath();

            // The game's enemies stand still through the director's start pause (SemiFunc.EnemySpawnIdlePause); the
            // Whispral goes looking for the team at once (ADR-0008).
            if (_startPauseSkipReported || !SemiFunc.EnemySpawnIdlePause()) return;
            _startPauseSkipReported = true;
            LOG.Debug($"Whispral {Whispral._photonView.ViewID} skips the director's start pause " +
                      $"({EnemyDirector.instance.spawnIdlePauseTimer:F0} s left).");
        }

        public override void OnStateUpdate()
        {
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

            var position = Whispral.transform.position;
            var roamPoint = Presence.RoamPoint(
                SemiFunc.LevelPointGetPlayerDistance(position, 18f, 35f),
                SemiFunc.LevelPointGet(position, 10f, 25f),
                SemiFunc.LevelPointGet(position, 0f, 999f));
            if (roamPoint is not { } picked)
            {
                LOG.Debug($"Whispral {Whispral._photonView.ViewID} finds no roam point.");
                return;
            }

            var (levelPoint, source) = picked;
            var pointPosition = levelPoint.transform.position;
            var pointDistance = NearestPlayerDistance(pointPosition);
            if (!SamplePosition(levelPoint, out var hit) || !Raycast(hit))
            {
                LOG.Debug($"Whispral {Whispral._photonView.ViewID} finds no NavMesh near {pointPosition} ({source}, " +
                          $"{pointDistance:F1} m from the nearest alive player).");
                return;
            }

            LOG.Debug($"Whispral {Whispral._photonView.ViewID} roams to {pointPosition} ({source}), " +
                      $"{pointDistance:F1} m from the nearest alive player.");
            Enemy.NavMeshAgent.SetDestination(hit.position);
            Enemy.Rigidbody.notMovingTimer = 0f;
            _hasDestination = true;
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
        private int? _victimViewID;
        private float _voiceTimer;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            Whispral.attachedTimer = Whispral.attachedDuration;
            _voiceTimer = GetRandomVoiceDelay();
            var victim = Whispral.playerTarget;
            if (victim == null) return;

            Whispral.attachAnchor = SemiFunc.PlayerGetFaceEyeTransform(victim);
            // Kept as an ID: the Victim's avatar may be gone by the time the Attachment ends.
            _victimViewID = victim.photonView.ViewID;
            LOG.Debug($"Whispral {Whispral._photonView.ViewID} starts its Attachment to {victim.playerName}.");
            Whispral.ReportAttachment(_victimViewID.Value, true);
        }

        // The one place an Attachment ends, whatever the next state: no other state reports it.
        public override void OnStateExit(State next)
        {
            base.OnStateExit(next);
            Whispral.attachAnchor = null;
            if (_victimViewID == null) return;

            LOG.Debug($"Whispral {Whispral._photonView.ViewID} ends its Attachment to ViewID {_victimViewID} (-> {next}).");
            Whispral.ReportAttachment(_victimViewID.Value, false);
            _victimViewID = null;
        }

        public override void OnStateUpdate()
        {
            if (CheckLeaveState()) return;
            CheckDetachState();

            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            _voiceTimer -= Time.deltaTime;
            if (!(_voiceTimer <= 0f)) return;
            SendVoiceCommand();
            _voiceTimer = GetRandomVoiceDelay();
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
            var victim = Whispral.playerTarget;
            if (victim == null) return;

            if (!SemiFunc.PlayerGetList().Any(p => p && p != victim))
            {
                LOG.Debug($"No Mimic command to {victim.playerName}: they are alone in the room.");
                return;
            }

            Whispral.SendMimicCommand(victim, Hallucination.RollVoiceFilter());
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
        }

        public override void OnStateUpdate()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Fsm.NextStateStateId = State.DetachWait;
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

            var levelPoint = SemiFunc.LevelPointGetPlayerDistance(Whispral.transform.position, 30f, 50f);
            if (levelPoint == null) levelPoint = SemiFunc.LevelPointGetFurthestFromPlayer(Whispral.transform.position, 5f);

            if (levelPoint &&
                NavMesh.SamplePosition(levelPoint.transform.position + Random.insideUnitSphere * 3f,
                    out var hit, 5f, -1) &&
                Physics.Raycast(hit.position, Vector3.down, 5f, LayerMask.GetMask("Default")))
            {
                Whispral.agentDestination = hit.position;
                _hasDestination = true;
            }

            // No SpawnedTimerSet: a flee does not despawn the Whispral (ADR-0008).
            SemiFunc.EnemyLeaveStart(Enemy);
        }

        public override void OnStateExit(State next)
        {
            base.OnStateExit(next);
            if (next == State.Despawn) return;

            LOG.Debug($"Whispral {Whispral._photonView.ViewID} ends its flee (-> {next}) still present, " +
                      $"spawn timer {Enemy.EnemyParent.SpawnedTimer:F1} s.");
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
            LOG.Debug($"Whispral {Whispral._photonView.ViewID} stunned in state {previous}.");
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
            Enemy.EnemyParent.Despawn();
            LOG.Debug($"Whispral {Whispral._photonView.ViewID} despawns from state {previous} " +
                      $"(game's despawned timer {Enemy.EnemyParent.DespawnedTimer:F0} s).");
            Fsm.NextStateStateId = State.Spawn;
        }
    }

    #endregion
}
