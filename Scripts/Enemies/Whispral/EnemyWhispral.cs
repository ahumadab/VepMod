using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace VepMod.Scripts.Enemies.Whispral;

public class EnemyWhispral : MonoBehaviour
{
    public enum State
    {
        Spawn = 0,
        Idle = 1,
        Roam = 2,
        Investigate = 3,
        PlayerNotice = 4,
        PlayerGoTo = 5,
        AttachPrepare = 6, // proche du PlayerPickup
        Attached = 7, // collé au joueur, pas d’attaque
        Detach = 8, // petite phase de détachage
        DetachWait = 9, // buffer avant de repartir
        Leave = 10,
        Stun = 11,
        StunEnd = 12,
        Despawn = 13
    }

    [Space] public State currentState;

    [Space] public Enemy enemy;

    public EnemyWhispralAnim enemyWhispralAnim;

    [Header("Attach settings")] [Tooltip("Temps pendant lequel l’ennemi reste collé au joueur.")]
    public float attachedDuration = 20f;

    [Space] public SpringQuaternion rotationSpring;

    private Vector3 agentDestination;

    private bool agentSet;
    private Transform attachAnchor;

    private float attachedTimer;
    private float grabAggroTimer; // comme EnemyHidden

    private PhotonView photonView;
    private PlayerAvatar playerTarget;
    private Quaternion rotationTarget;

    /// <summary>
    ///     Flag “première frame dans cet état”
    /// </summary>
    private bool stateImpulse;

    /// <summary>
    ///     Compte à rebours associé à l’état courant - "combien de temps il me reste avant de faire quelque chose d’autre dans
    ///     cet état"
    /// </summary>
    private float stateTimer;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
    }

    private void Update()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            return;
        }

        if (grabAggroTimer > 0f)
        {
            grabAggroTimer -= Time.deltaTime;
        }

        RotationLogic();

        if (enemy.IsStunned())
        {
            UpdateState(State.Stun);
        }

        if (enemy.CurrentState == EnemyState.Despawn && !enemy.IsStunned())
        {
            UpdateState(State.Despawn);
        }

        switch (currentState)
        {
            case State.Spawn:
                StateSpawn();
                break;
            case State.Idle:
                StateIdle();
                break;
            case State.Roam:
                StateRoam();
                break;
            case State.Investigate:
                StateInvestigate();
                break;
            case State.PlayerNotice:
                StatePlayerNotice();
                break;
            case State.PlayerGoTo:
                StatePlayerGoTo();
                break;
            case State.AttachPrepare:
                StateAttachPrepare();
                break;
            case State.Attached:
                StateAttached();
                break;
            case State.Detach:
                StateDetach();
                break;
            case State.DetachWait:
                StateDetachWait();
                break;
            case State.Leave:
                StateLeave();
                break;
            case State.Stun:
                StateStun();
                break;
            case State.StunEnd:
                StateStunEnd();
                break;
            case State.Despawn:
                StateDespawn();
                break;
        }
    }

    private void FixedUpdate()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            return;
        }

        AttachFollowLogic();
    }

    #region États de base (copiés de EnemyHidden)

    private void StateSpawn()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            stateTimer = 1f;
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            UpdateState(State.Idle);
        }
    }

    private void StateIdle()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            stateTimer = Random.Range(2f, 5f);
            enemy.NavMeshAgent.Warp(enemy.Rigidbody.transform.position);
            enemy.NavMeshAgent.ResetPath();
        }

        if (!SemiFunc.EnemySpawnIdlePause())
        {
            stateTimer -= Time.deltaTime;
            if (stateTimer <= 0f)
            {
                UpdateState(State.Roam);
            }

            if (SemiFunc.EnemyForceLeave(enemy))
            {
                UpdateState(State.Leave);
            }
        }
    }

    private void StateRoam()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            stateTimer = 5f;
            var found = false;
            var levelPoint = SemiFunc.LevelPointGet(transform.position, 10f, 25f);
            if (!levelPoint)
            {
                levelPoint = SemiFunc.LevelPointGet(transform.position, 0f, 999f);
            }

            if (levelPoint &&
                NavMesh.SamplePosition(levelPoint.transform.position + Random.insideUnitSphere * 3f, out var hit,
                    5f, -1) &&
                Physics.Raycast(hit.position, Vector3.down, 5f, LayerMask.GetMask("Default")))
            {
                enemy.NavMeshAgent.SetDestination(hit.position);
                found = true;
            }

            if (!found)
            {
                return;
            }

            enemy.Rigidbody.notMovingTimer = 0f;
        }
        else
        {
            SemiFunc.EnemyCartJump(enemy);
            if (enemy.Rigidbody.notMovingTimer > 2f)
            {
                stateTimer -= Time.deltaTime;
            }

            if (stateTimer <= 0f || !enemy.NavMeshAgent.HasPath())
            {
                SemiFunc.EnemyCartJumpReset(enemy);
                UpdateState(State.Idle);
            }
        }

        if (SemiFunc.EnemyForceLeave(enemy))
        {
            UpdateState(State.Leave);
        }
    }

    private void StateInvestigate()
    {
        if (stateImpulse)
        {
            stateTimer = 5f;
            enemy.Rigidbody.notMovingTimer = 0f;
            stateImpulse = false;
        }
        else
        {
            enemy.NavMeshAgent.SetDestination(agentDestination);
            SemiFunc.EnemyCartJump(enemy);
            if (enemy.Rigidbody.notMovingTimer > 2f)
            {
                stateTimer -= Time.deltaTime;
            }

            if (stateTimer <= 0f || !enemy.NavMeshAgent.HasPath())
            {
                SemiFunc.EnemyCartJumpReset(enemy);
                UpdateState(State.Idle);
            }
        }

        if (SemiFunc.EnemyForceLeave(enemy))
        {
            UpdateState(State.Leave);
        }
    }

    private void StatePlayerNotice()
    {
        if (stateImpulse)
        {
            enemy.NavMeshAgent.Warp(enemy.Rigidbody.transform.position);
            enemy.NavMeshAgent.ResetPath();
            stateImpulse = false;
            stateTimer = 2f;
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            UpdateState(State.PlayerGoTo);
        }
    }

    private void StatePlayerGoTo()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            stateTimer = 2f;
            agentSet = true;
        }

        stateTimer -= Time.deltaTime;
        if (!playerTarget || playerTarget.isDisabled || stateTimer <= 0f)
        {
            UpdateState(State.Leave);
            return;
        }

        SemiFunc.EnemyCartJump(enemy);
        if (enemy.Jump.jumping)
        {
            enemy.NavMeshAgent.Disable(0.5f);
            transform.position = Vector3.MoveTowards(transform.position, playerTarget.transform.position,
                5f * Time.deltaTime);
            agentSet = true;
        }
        else if (!enemy.NavMeshAgent.IsDisabled())
        {
            if (!agentSet && enemy.NavMeshAgent.HasPath() &&
                Vector3.Distance(enemy.Rigidbody.transform.position + Vector3.down * 0.75f,
                    enemy.NavMeshAgent.GetDestination()) < 0.25f)
            {
                enemy.Jump.StuckTrigger(enemy.Rigidbody.transform.position - playerTarget.transform.position);
            }

            enemy.NavMeshAgent.SetDestination(playerTarget.transform.position);
            enemy.NavMeshAgent.OverrideAgent(5f, 10f, 0.25f);
            agentSet = false;
        }

        if (Vector3.Distance(enemy.Rigidbody.transform.position, playerTarget.transform.position) < 1.5f)
        {
            SemiFunc.EnemyCartJumpReset(enemy);
            UpdateState(State.AttachPrepare);
        }
    }

    #endregion

    #region Nouveaux états d’attache

    /// <summary>
    ///     Petite phase avant de se coller au joueur (ancien PlayerPickup).
    /// </summary>
    private void StateAttachPrepare()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            stateTimer = 1f;
            // On s'assure que l’agent navmesh est “figé” au bon endroit
            enemy.NavMeshAgent.Warp(enemy.Rigidbody.transform.position);
            enemy.NavMeshAgent.ResetPath();
        }

        if (!playerTarget || playerTarget.isDisabled)
        {
            UpdateState(State.Leave);
            return;
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            UpdateState(State.Attached);
        }
    }

    /// <summary>
    ///     Ennemi collé au joueur : il ne le porte pas, il le “suit” simplement.
    ///     Pas d’attaque dans cet état.
    /// </summary>
    private void StateAttached()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            attachedTimer = attachedDuration;

            // On prend comme “anchor” le visage/yeux du player (comme SlowMouth)
            attachAnchor = playerTarget
                ? SemiFunc.PlayerGetFaceEyeTransform(playerTarget)
                : null;

            // TODO : ici tu peux déclencher le début des hallucinations
            // ex: si playerTarget.isLocal => activer tes effets visu/son sur ce client
            // OnAttachedToPlayer();
        }

        if (!playerTarget || playerTarget.isDisabled)
        {
            UpdateState(State.Leave);
            return;
        }

        attachedTimer -= Time.deltaTime;
        if (attachedTimer <= 0f)
        {
            UpdateState(State.Detach);
        }
    }

    /// <summary>
    ///     Phase de détache : petite transition avant de repartir.
    /// </summary>
    private void StateDetach()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            stateTimer = 0.5f;

            // On arrête de “coller” strictement le joueur, l’ennemi reste à sa position actuelle
            enemy.NavMeshAgent.Warp(enemy.Rigidbody.transform.position);
            enemy.NavMeshAgent.ResetPath();

            // TODO : ici tu peux désactiver tes hallucinations
            // OnDetachedFromPlayer();
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            attachAnchor = null;
            UpdateState(State.DetachWait);
        }
    }

    /// <summary>
    ///     Petit délai après le détache pour éviter de ré-aggro instant.
    /// </summary>
    private void StateDetachWait()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            stateTimer = 2f;
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            UpdateState(State.Leave);
        }
    }

    #endregion

    #region Leave / stun / despawn (copiés de EnemyHidden)

    private void StateLeave()
    {
        if (stateImpulse)
        {
            stateTimer = 5f;
            var found = false;
            var levelPoint = SemiFunc.LevelPointGetPlayerDistance(transform.position, 30f, 50f);
            if (!levelPoint)
            {
                levelPoint = SemiFunc.LevelPointGetFurthestFromPlayer(transform.position, 5f);
            }

            if (levelPoint &&
                NavMesh.SamplePosition(levelPoint.transform.position + Random.insideUnitSphere * 3f, out var hit,
                    5f, -1) &&
                Physics.Raycast(hit.position, Vector3.down, 5f, LayerMask.GetMask("Default")))
            {
                agentDestination = hit.position;
                found = true;
            }

            SemiFunc.EnemyLeaveStart(enemy);
            if (!found)
            {
                return;
            }

            stateImpulse = false;
            enemy.EnemyParent.SpawnedTimerSet(1f);
        }

        if (enemy.Rigidbody.notMovingTimer > 2f)
        {
            stateTimer -= Time.deltaTime;
        }

        enemy.NavMeshAgent.SetDestination(agentDestination);
        enemy.NavMeshAgent.OverrideAgent(5f, 10f, 0.25f);
        SemiFunc.EnemyCartJump(enemy);

        if (Vector3.Distance(transform.position, agentDestination) < 1f || stateTimer <= 0f)
        {
            SemiFunc.EnemyCartJumpReset(enemy);
            UpdateState(State.Idle);
        }
    }

    private void StateStun()
    {
        if (!enemy.IsStunned())
        {
            UpdateState(State.StunEnd);
        }
    }

    private void StateStunEnd()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            stateTimer = 1f;
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            UpdateState(State.Leave);
        }
    }

    private void StateDespawn()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            enemy.EnemyParent.Despawn();
            UpdateState(State.Spawn);
        }
    }

    #endregion

    #region API publique (hooks du système Enemy)

    public void OnSpawn()
    {
        if (SemiFunc.IsMasterClientOrSingleplayer() && SemiFunc.EnemySpawn(enemy))
        {
            UpdateState(State.Spawn);
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
        if (SemiFunc.IsMasterClientOrSingleplayer() &&
            (currentState == State.Idle || currentState == State.Roam || currentState == State.Investigate))
        {
            agentDestination = enemy.StateInvestigate.onInvestigateTriggeredPosition;
            UpdateState(State.Investigate);
        }
    }

    public void OnVision()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            return;
        }

        if (currentState == State.Idle || currentState == State.Roam ||
            currentState == State.Investigate || currentState == State.Leave)
        {
            playerTarget = enemy.Vision.onVisionTriggeredPlayer;
            if (SemiFunc.IsMultiplayer())
            {
                photonView.RPC("UpdatePlayerTargetRPC", RpcTarget.All, playerTarget.photonView.ViewID);
            }

            UpdateState(State.PlayerNotice);
        }
        else if (currentState == State.PlayerGoTo)
        {
            stateTimer = 2f;
        }
    }

    public void OnGrabbed()
    {
        if (SemiFunc.IsMasterClientOrSingleplayer() &&
            !(grabAggroTimer > 0f) && currentState == State.Leave)
        {
            grabAggroTimer = 60f;
            playerTarget = enemy.Rigidbody.onGrabbedPlayerAvatar;
            if (SemiFunc.IsMultiplayer())
            {
                photonView.RPC("UpdatePlayerTargetRPC", RpcTarget.All, playerTarget.photonView.ViewID);
            }

            UpdateState(State.PlayerNotice);
        }
    }

    /// <summary>
    ///     Si tu veux forcer le détache depuis un autre script (ex : quand le joueur passe en forme objet),
    ///     tu peux appeler cette méthode.
    /// </summary>
    public void ForceDetach()
    {
        if (currentState == State.AttachPrepare || currentState == State.Attached)
        {
            UpdateState(State.Detach);
        }
    }

    #endregion

    #region State machine helpers

    private void UpdateState(State newState)
    {
        if (currentState == newState)
        {
            return;
        }

        enemy.Rigidbody.StuckReset();
        currentState = newState;
        stateImpulse = true;
        stateTimer = 0f;

        if (currentState == State.Leave)
        {
            SemiFunc.EnemyLeaveStart(enemy);
        }

        if (GameManager.Multiplayer())
        {
            photonView.RPC("UpdateStateRPC", RpcTarget.All, currentState);
        }
        else
        {
            UpdateStateRPC(currentState);
        }
    }

    private void RotationLogic()
    {
        if ((currentState == State.PlayerNotice || currentState == State.PlayerGoTo ||
             currentState == State.AttachPrepare)
            && playerTarget)
        {
            if (Vector3.Distance(playerTarget.transform.position, transform.position) > 0.1f)
            {
                rotationTarget = Quaternion.LookRotation(playerTarget.transform.position - transform.position);
                rotationTarget.eulerAngles = new Vector3(0f, rotationTarget.eulerAngles.y, 0f);
            }
        }
        else if (enemy.NavMeshAgent.AgentVelocity.normalized.magnitude > 0.1f)
        {
            rotationTarget = Quaternion.LookRotation(enemy.NavMeshAgent.AgentVelocity.normalized);
            rotationTarget.eulerAngles = new Vector3(0f, rotationTarget.eulerAngles.y, 0f);
        }

        transform.rotation = SemiFunc.SpringQuaternionGet(rotationSpring, rotationTarget);
    }

    /// <summary>
    ///     Ici on suit le joueur quand on est en Attached, mais on ne le déplace jamais.
    /// </summary>
    private void AttachFollowLogic()
    {
        if (currentState != State.Attached)
        {
            return;
        }

        if (!playerTarget || playerTarget.isDisabled || !attachAnchor)
        {
            return;
        }

        // Position cible : un peu au-dessus et derrière la tête
        var desiredPos = attachAnchor.position
                         + attachAnchor.TransformDirection(new Vector3(0f, 0.3f, -0.3f));

        enemy.Rigidbody.transform.position =
            Vector3.Lerp(enemy.Rigidbody.transform.position, desiredPos, 10f * Time.fixedDeltaTime);

        var desiredRot = Quaternion.LookRotation(attachAnchor.forward, Vector3.up);
        enemy.Rigidbody.transform.rotation =
            Quaternion.Slerp(enemy.Rigidbody.transform.rotation, desiredRot, 10f * Time.fixedDeltaTime);
    }

    #endregion

    #region RPC

    [PunRPC]
    private void UpdateStateRPC(State _state, PhotonMessageInfo _info = default)
    {
        if (SemiFunc.MasterOnlyRPC(_info))
        {
            currentState = _state;
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
                // On récupère un point “face/yeux” pour l’attache, comme dans EnemySlowMouth
                attachAnchor = SemiFunc.PlayerGetFaceEyeTransform(item);
                break;
            }
        }
    }

    #endregion
}