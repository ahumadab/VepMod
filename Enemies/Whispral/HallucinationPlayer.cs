using UnityEngine;
using UnityEngine.AI;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Représente une hallucination de joueur qui se balade au hasard.
///     Visible uniquement par le joueur local affecté par le Whispral.
///     Utilise le système d'animation et de sons du jeu pour un rendu réaliste.
/// </summary>
public sealed class HallucinationPlayer : MonoBehaviour
{
    private const float MinRoamTime = 3f;
    private const float MaxRoamTime = 8f;
    private const float MinIdleTime = 1f;
    private const float MaxIdleTime = 3f;
    private const float MinRoamDistance = 10f;
    private const float MaxRoamDistance = 25f;
    private const float WalkSpeedThreshold = 0.1f;

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationPlayer>();

    private NavMeshAgent navAgent;
    private GameObject visualsRoot;
    private Light flashlight;
    private Animator animator;
    private HallucinationAnimEventReceiver animEventReceiver;

    private State currentState = State.Idle;
    private float stateTimer;

    // État d'animation actuel
    private bool isMoving;
    private bool isSprinting;
    private bool isCrouching;

    public PlayerAvatar SourcePlayer { get; private set; }

    /// <summary>
    ///     Initialise l'hallucination avec l'apparence du joueur source.
    /// </summary>
    public void Initialize(PlayerAvatar sourcePlayer, Vector3 spawnPosition)
    {
        SourcePlayer = sourcePlayer;

        // Positionner le transform à la position de spawn
        transform.position = spawnPosition;

        // Configurer le NavMeshAgent
        if (!SetupNavMeshAgent(spawnPosition))
        {
            LOG.Warning($"Could not setup NavMeshAgent for {sourcePlayer.playerName}");
            Destroy(gameObject);
            return;
        }

        // Copier l'apparence du joueur (avec Animator actif)
        SetupVisuals(sourcePlayer);

        // Créer la lampe torche
        SetupFlashlight();

        // Démarrer en idle
        EnterIdleState();

        LOG.Debug($"Hallucination created for {sourcePlayer.playerName} at {spawnPosition}");
    }

    private void Update()
    {
        if (!navAgent || !navAgent.isOnNavMesh) return;

        stateTimer -= Time.deltaTime;

        switch (currentState)
        {
            case State.Idle:
                UpdateIdleState();
                break;
            case State.Roam:
                UpdateRoamState();
                break;
        }

        UpdateAnimations();
        UpdateFlashlightDirection();
        UpdateRotation();
    }

    private void OnDestroy()
    {
        if (visualsRoot)
        {
            Destroy(visualsRoot);
        }
    }

    #region Setup

    private bool SetupNavMeshAgent(Vector3 targetPosition)
    {
        navAgent = gameObject.AddComponent<NavMeshAgent>();
        navAgent.enabled = false;

        // Paramètres similaires à un joueur qui marche
        navAgent.speed = 2.5f;
        navAgent.acceleration = 8f;
        navAgent.angularSpeed = 120f;
        navAgent.stoppingDistance = 0.5f;
        navAgent.autoBraking = true;
        navAgent.radius = 0.3f;
        navAgent.height = 1.8f;

        transform.position = targetPosition;
        navAgent.enabled = true;
        navAgent.Warp(targetPosition);

        return true;
    }

    private void SetupVisuals(PlayerAvatar sourcePlayer)
    {
        if (!sourcePlayer.playerAvatarVisuals || !sourcePlayer.playerAvatarVisuals.meshParent)
        {
            LOG.Warning($"Source player {sourcePlayer.playerName} has no visuals to copy");
            return;
        }

        // Copier le mesh parent du joueur
        var sourceMesh = sourcePlayer.playerAvatarVisuals.meshParent;
        visualsRoot = Instantiate(sourceMesh, transform);
        visualsRoot.name = "HallucinationVisuals";
        visualsRoot.transform.localPosition = Vector3.zero;
        visualsRoot.transform.localRotation = Quaternion.identity;
        visualsRoot.SetActive(true);

        // Configurer les composants
        ConfigureComponents(visualsRoot);

        // Récupérer l'Animator pour le contrôler
        animator = visualsRoot.GetComponentInChildren<Animator>();
        if (animator)
        {
            animator.enabled = true;
            LOG.Debug("Animator found and enabled for hallucination");

            // Ajouter le récepteur d'Animation Events sur le même GameObject que l'Animator
            // pour que les Animation Events soient bien reçus
            animEventReceiver = animator.gameObject.AddComponent<HallucinationAnimEventReceiver>();
            animEventReceiver.Initialize(this);
            LOG.Debug("Animation event receiver attached");
        }
    }

    private static void ConfigureComponents(GameObject root)
    {
        // Désactiver les colliders (pas de collision)
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        // Désactiver les Rigidbodies
        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
        }

        // Supprimer les scripts du joueur qui pourraient interférer
        // mais garder l'Animator actif
        foreach (var playerScript in root.GetComponentsInChildren<PlayerAvatarVisuals>(true))
        {
            Object.Destroy(playerScript);
        }
    }

    private void SetupFlashlight()
    {
        var flashlightGO = new GameObject("HallucinationFlashlight");
        flashlightGO.transform.SetParent(transform);
        flashlightGO.transform.localPosition = new Vector3(0f, 1.5f, 0.3f);
        flashlightGO.transform.localRotation = Quaternion.identity;

        flashlight = flashlightGO.AddComponent<Light>();
        flashlight.type = LightType.Spot;
        flashlight.color = new Color(1f, 0.95f, 0.8f);
        flashlight.intensity = 2f;
        flashlight.range = 15f;
        flashlight.spotAngle = 60f;
        flashlight.innerSpotAngle = 30f;
        flashlight.shadows = LightShadows.None;
    }

    #endregion

    #region State Machine

    private void EnterIdleState()
    {
        currentState = State.Idle;
        stateTimer = Random.Range(MinIdleTime, MaxIdleTime);

        if (navAgent && navAgent.isOnNavMesh)
        {
            navAgent.ResetPath();
        }
    }

    private void EnterRoamState()
    {
        currentState = State.Roam;
        stateTimer = Random.Range(MinRoamTime, MaxRoamTime);

        if (!TrySetRandomDestination())
        {
            EnterIdleState();
        }
    }

    private void UpdateIdleState()
    {
        if (stateTimer <= 0f)
        {
            EnterRoamState();
        }
    }

    private void UpdateRoamState()
    {
        var reachedDestination = !navAgent.hasPath ||
                                 navAgent.remainingDistance < 1f;

        if (reachedDestination || stateTimer <= 0f)
        {
            EnterIdleState();
        }
    }

    private bool TrySetRandomDestination()
    {
        var levelPoint = SemiFunc.LevelPointGet(transform.position, MinRoamDistance, MaxRoamDistance)
                         ?? SemiFunc.LevelPointGet(transform.position, 0f, 999f);

        if (!levelPoint) return false;

        var destination = levelPoint.transform.position;
        var path = new NavMeshPath();

        if (navAgent.CalculatePath(destination, path) &&
            (path.status == NavMeshPathStatus.PathComplete || path.status == NavMeshPathStatus.PathPartial))
        {
            navAgent.SetPath(path);
            return true;
        }

        navAgent.SetDestination(destination);
        return navAgent.hasPath;
    }

    #endregion

    #region Animation

    private void UpdateAnimations()
    {
        if (!animator) return;

        // Détecter l'état de mouvement basé sur la vélocité du NavMeshAgent
        var velocity = navAgent.velocity.magnitude;
        var currentlyMoving = velocity > WalkSpeedThreshold;

        // Mettre à jour l'état
        if (currentlyMoving != isMoving)
        {
            isMoving = currentlyMoving;
        }

        // Définir les paramètres d'animation du joueur (noms exacts du jeu REPO)
        // Ces paramètres correspondent à ceux dans PlayerAvatarVisuals.AnimationLogic()
        animator.SetBool("Moving", isMoving);
        animator.SetBool("Sprinting", isSprinting);
        animator.SetBool("Crouching", isCrouching);
        animator.SetBool("Crawling", false);
        animator.SetBool("Sliding", false);
        animator.SetBool("Tumbling", false);
        animator.SetBool("TumblingMove", false);
        animator.SetBool("Jumping", false);
        animator.SetBool("Falling", false);
        animator.SetBool("Grabbing", false);

        // Turning - basé sur la rotation
        var turning = navAgent.velocity.magnitude > WalkSpeedThreshold &&
                      Vector3.Angle(transform.forward, navAgent.velocity.normalized) > 15f;
        animator.SetBool("Turning", turning && !isMoving);
    }

    private void UpdateFlashlightDirection()
    {
        if (!flashlight) return;

        var direction = navAgent.velocity.magnitude > WalkSpeedThreshold
            ? navAgent.velocity.normalized
            : transform.forward;

        direction.y = -0.2f;
        flashlight.transform.rotation = Quaternion.LookRotation(direction.normalized);
    }

    private void UpdateRotation()
    {
        // Faire tourner le modèle vers la direction de mouvement
        if (navAgent.velocity.magnitude > WalkSpeedThreshold)
        {
            var targetRotation = Quaternion.LookRotation(navAgent.velocity.normalized);
            targetRotation = Quaternion.Euler(0f, targetRotation.eulerAngles.y, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
        }
    }

    #endregion

    private enum State
    {
        Idle,
        Roam
    }
}
