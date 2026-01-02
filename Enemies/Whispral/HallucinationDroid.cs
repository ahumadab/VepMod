using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using VepMod.VepFramework;
using VepMod.VepFramework.Structures.FSM;
using Random = UnityEngine.Random;

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

    private const float NameplateHeight = 1.9f;

    private const float TalkRotationMaxAngle = 25f;
    private const int SampleDataLength = 256;

    // Angry eyes settings
    private const float LookAtAngle = 60f; // Angle de vision du joueur
    private const float LookAtMaxDistance = 30f; // Distance max de détection
    private const float AngryEyesCooldown = 0.6f; // Temps avant d'annuler l'emote après être vu

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationDroid>(true);
    private readonly Materials.MaterialTrigger _materialTrigger = new();
    private float _angryTimer;
    private Animator _animator;
    private CharacterController _charController;

    private Vector3 _currentVelocity;

    private Vector3 _destination;

    // Angry eyes - eyelid GameObjects and rotation transforms
    private GameObject _eyelidsLeft;
    private GameObject _eyelidsRight;
    private Transform _headTopTransform;
    private bool _isAngry;
    private bool _isPrecomputingDestination;
    private Transform _leftLowerEyelidRotationX;
    private Transform _leftLowerEyelidRotationZ;
    private Transform _leftUpperEyelidRotationX;
    private Transform _leftUpperEyelidRotationZ;

    private WorldSpaceUIPlayerName _nameplate;
    private NavMeshAgent _navAgent;

    // Destination pré-calculée pour éviter les freezes
    private Vector3? _precomputedDestination;
    private Transform _rightLowerEyelidRotationX;
    private Transform _rightLowerEyelidRotationZ;
    private Transform _rightUpperEyelidRotationX;
    private Transform _rightUpperEyelidRotationZ;
    private Transform _rigidbodyTransform;
    private float[] _sampleData;

    private int _savedAgentTypeID;
    private int _savedAreaMask = NavMesh.AllAreas;
    private AudioSource _talkingAudioSource;
    private Quaternion _targetRotation = Quaternion.identity;
    private bool _wasPlayerLooking;

    public bool IsWalking { get; set; }
    public bool IsSprinting { get; set; }
    public bool IsTurning { get; private set; }
    public bool HasChangedMovementState { get; set; }
    public PlayerAvatar SourcePlayer { get; private set; }
    public Transform ControllerTransform { get; private set; }

    protected override StateId DefaultState => StateId.Idle;

    protected override void Awake()
    {
        // Ne pas appeler base.Awake() ici - on initialise la FSM dans Initialize()
    }

    protected override void Update()
    {
        if (_navAgent == null || !_navAgent.isOnNavMesh) return;
        if (_charController == null || ControllerTransform == null) return;

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
        if ((currentState == StateId.Wander || currentState == StateId.Sprint) && ControllerTransform != null)
        {
            var angle = Quaternion.Angle(ControllerTransform.rotation, _targetRotation);
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

    /// <summary>
    ///     Vérifie si le joueur local regarde en direction du droid.
    /// </summary>
    private bool IsPlayerLookingAtMe()
    {
        var camera = Camera.main;
        if (camera == null || ControllerTransform == null) return false;

        var droidPosition = ControllerTransform.position + Vector3.up; // Centre du droid
        var cameraPosition = camera.transform.position;

        // Check distance
        var distance = Vector3.Distance(cameraPosition, droidPosition);
        if (distance > LookAtMaxDistance) return false;

        // Check angle
        var directionToDroid = (droidPosition - cameraPosition).normalized;
        var dot = Vector3.Dot(camera.transform.forward, directionToDroid);
        var threshold = Mathf.Cos(LookAtAngle * Mathf.Deg2Rad);

        return dot >= threshold;
    }

    // Angry expression values
    private static class AngryExpression
    {
        public const float UpperLidAngleLeft = -34f;
        public const float UpperLidAngleRight = 34f;
        public const float UpperLidClosed = 83f;
        public const float LowerLidClosed = -83f;
    }

    /// <summary>
    ///     Met à jour l'état des yeux fâchés.
    ///     - Le droid montre les paupières fâchées quand le joueur ne le regarde pas
    ///     - Il les cache X secondes après avoir été vu
    /// </summary>
    private void UpdateAngryEyes()
    {
        if (_eyelidsLeft == null || _eyelidsRight == null) return;
        if (_leftUpperEyelidRotationX == null || _rightUpperEyelidRotationX == null) return;

        var isPlayerLooking = IsPlayerLookingAtMe();

        // Transition de "pas regardé" à "regardé"
        if (isPlayerLooking && !_wasPlayerLooking)
        {
            _angryTimer = AngryEyesCooldown;
        }

        _wasPlayerLooking = isPlayerLooking;

        // Gestion du timer et de l'état angry
        if (isPlayerLooking)
        {
            _angryTimer -= Time.deltaTime;
            if (_angryTimer <= 0f)
            {
                _isAngry = false;
            }
        }
        else
        {
            _isAngry = true;
        }

        // Activer les paupières quand on devient angry
        if (_isAngry && !_eyelidsLeft.activeSelf)
        {
            _eyelidsLeft.SetActive(true);
            _eyelidsRight.SetActive(true);
        }

        // Valeurs cibles
        var targetUpperClosed = _isAngry ? AngryExpression.UpperLidClosed : 0f;
        var targetLowerClosed = _isAngry ? AngryExpression.LowerLidClosed : 0f;
        var targetLeftAngle = _isAngry ? AngryExpression.UpperLidAngleLeft : 0f;
        var targetRightAngle = _isAngry ? AngryExpression.UpperLidAngleRight : 0f;

        var lerpSpeed = Time.deltaTime * 8f;

        // Animer les paupières supérieures
        _leftUpperEyelidRotationX.localRotation = Quaternion.Slerp(
            _leftUpperEyelidRotationX.localRotation,
            Quaternion.Euler(targetUpperClosed, 0f, 0f),
            lerpSpeed);

        _rightUpperEyelidRotationX.localRotation = Quaternion.Slerp(
            _rightUpperEyelidRotationX.localRotation,
            Quaternion.Euler(targetUpperClosed, 0f, 0f),
            lerpSpeed);

        if (_leftUpperEyelidRotationZ != null)
        {
            _leftUpperEyelidRotationZ.localRotation = Quaternion.Slerp(
                _leftUpperEyelidRotationZ.localRotation,
                Quaternion.Euler(0f, 0f, targetLeftAngle),
                lerpSpeed);
        }

        if (_rightUpperEyelidRotationZ != null)
        {
            _rightUpperEyelidRotationZ.localRotation = Quaternion.Slerp(
                _rightUpperEyelidRotationZ.localRotation,
                Quaternion.Euler(0f, 0f, targetRightAngle),
                lerpSpeed);
        }

        // Animer les paupières inférieures
        if (_leftLowerEyelidRotationX != null)
        {
            _leftLowerEyelidRotationX.localRotation = Quaternion.Slerp(
                _leftLowerEyelidRotationX.localRotation,
                Quaternion.Euler(targetLowerClosed, 0f, 0f),
                lerpSpeed);
        }

        if (_rightLowerEyelidRotationX != null)
        {
            _rightLowerEyelidRotationX.localRotation = Quaternion.Slerp(
                _rightLowerEyelidRotationX.localRotation,
                Quaternion.Euler(targetLowerClosed, 0f, 0f),
                lerpSpeed);
        }

        // Désactiver les paupières quand l'animation est terminée (retour à la normale)
        if (!_isAngry && _eyelidsLeft.activeSelf)
        {
            var currentAngle = Mathf.Abs(_leftUpperEyelidRotationX.localEulerAngles.x);
            if (currentAngle is < 10f or > 350f) // Proche de 0
            {
                _eyelidsLeft.SetActive(false);
                _eyelidsRight.SetActive(false);
            }
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
        if (_rigidbodyTransform == null || ControllerTransform == null) return;

        var yOffset = new Vector3(0f, 0.25f, 0f); // A GARDER, Necessaire pour bien ajuster les visuels
        _rigidbodyTransform.position = ControllerTransform.position + yOffset;
        _rigidbodyTransform.rotation = ControllerTransform.rotation;

        if (_animator != null && _animator.transform.localPosition != Vector3.zero)
        {
            _animator.transform.localPosition = Vector3.zero;
        }
    }

    private void SyncNavAgentPosition()
    {
        if (_navAgent == null || ControllerTransform == null) return;

        var controllerPos = ControllerTransform.position;

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
        if (ControllerTransform == null) return;

        if (_currentVelocity.magnitude > 0.1f)
        {
            var velocityDirection = _currentVelocity.normalized;
            velocityDirection.y = 0f;
            if (velocityDirection.sqrMagnitude > 0.01f)
            {
                _targetRotation = Quaternion.LookRotation(velocityDirection);
            }
        }

        ControllerTransform.rotation = Quaternion.Slerp(
            ControllerTransform.rotation,
            _targetRotation,
            RotationSpeed * Time.deltaTime);
    }

    public bool TrySetRandomDestination()
    {
        // Utiliser la destination pré-calculée si disponible
        if (_precomputedDestination.HasValue)
        {
            return TrySetDestination(_precomputedDestination.Value);
        }

        // Fallback synchrone (peut causer un freeze)
        LOG.Warning("No precomputed destination, falling back to sync search");
        return TrySetDestinationSync();
    }

    /// <summary>
    ///     Démarre le pré-calcul d'une destination en coroutine.
    ///     Appelé pendant l'état Idle pour préparer le prochain déplacement.
    /// </summary>
    public void StartPrecomputeDestination()
    {
        if (_isPrecomputingDestination || _precomputedDestination.HasValue) return;
        StartCoroutine(PrecomputeDestinationCoroutine());
    }

    /// <summary>
    ///     Indique si une destination pré-calculée est disponible.
    /// </summary>
    public bool HasPrecomputedDestination => _precomputedDestination.HasValue;

    private IEnumerator PrecomputeDestinationCoroutine()
    {
        _isPrecomputingDestination = true;

        // Frame 1: Trouver un LevelPoint
        var controllerPos = ControllerTransform != null ? ControllerTransform.position : transform.position;
        var levelPoint = SemiFunc.LevelPointGet(controllerPos, WhispralDebuffManager.SpawnDistanceMin,
                             WhispralDebuffManager.SpawnDistanceMax)
                         ?? SemiFunc.LevelPointGet(controllerPos, 0f, 20f);

        if (levelPoint == null)
        {
            _isPrecomputingDestination = false;
            yield break;
        }

        yield return null; // Attendre une frame

        // Frame 2: Valider sur le NavMesh
        var targetPos = levelPoint.transform.position;
        if (!NavMesh.SamplePosition(targetPos, out var hit, 5f, _savedAreaMask))
        {
            _isPrecomputingDestination = false;
            yield break;
        }

        yield return null; // Attendre une frame

        // Frame 3: Vérifier le raycast et calculer le path
        if (!Physics.Raycast(hit.position + Vector3.up, Vector3.down, 5f, LayerMask.GetMask("Default")))
        {
            _isPrecomputingDestination = false;
            yield break;
        }

        // Vérifier que le path est valide
        var path = new NavMeshPath();
        if (_navAgent != null && _navAgent.isOnNavMesh &&
            _navAgent.CalculatePath(hit.position, path) &&
            path.status == NavMeshPathStatus.PathComplete)
        {
            _precomputedDestination = hit.position;
        }

        _isPrecomputingDestination = false;
    }

    private bool TrySetDestination(Vector3 destination)
    {
        _precomputedDestination = null; // Consommer la destination

        if (_navAgent == null || !_navAgent.isOnNavMesh) return false;

        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(destination, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        _destination = destination;
        _navAgent.SetPath(path);
        return true;
    }

    private bool TrySetDestinationSync()
    {
        var controllerPos = ControllerTransform != null ? ControllerTransform.position : transform.position;

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

        var controllerPos = ControllerTransform != null ? ControllerTransform.position : transform.position;
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
        if (SourcePlayer == null || ControllerTransform == null) return;

        // Utiliser le WhispralMimics du joueur local (pas du source player)
        var localPlayer = PlayerAvatar.instance;
        if (localPlayer == null) return;

        var mimics = localPlayer.GetComponent<WhispralMimics>();
        if (mimics == null)
        {
            LOG.Warning("WhispralMimics not found on local player");
            return;
        }

        mimics.PlayAudioAtTransform(ControllerTransform, SourcePlayer.playerName, applyFilter);
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
        ApplyPlayerColor();
        CreateNameplate();
        FindHeadTransform();
        FindEyeTransforms();
        InitializeFSM();

        LOG.Info($"HallucinationDroid created for {sourcePlayer.playerName} at {ControllerTransform?.position}");
    }

    private void LateUpdate()
    {
        UpdateNameplate();
        UpdateTalkingAnimation();
        UpdateAngryEyes();
    }

    private void OnDestroy()
    {
        if (_nameplate != null)
        {
            Destroy(_nameplate.gameObject);
        }
    }

    private void UpdateTalkingAnimation()
    {
        if (_headTopTransform == null) return;

        var targetRotation = 0f;

        // Vérifier si on a un AudioSource actif sur le controller
        if (_talkingAudioSource == null && ControllerTransform != null)
        {
            _talkingAudioSource = ControllerTransform.GetComponent<AudioSource>();
        }

        if (_talkingAudioSource != null && _talkingAudioSource.isPlaying)
        {
            // Analyser le volume audio
            _sampleData ??= new float[SampleDataLength];
            _talkingAudioSource.GetOutputData(_sampleData, 0);

            var loudness = 0f;
            foreach (var sample in _sampleData)
            {
                loudness += Mathf.Abs(sample);
            }

            loudness /= SampleDataLength;

            // Calculer la rotation basée sur le volume (négatif = ouvrir vers le haut)
            if (loudness > 0.01f)
            {
                targetRotation = Mathf.Lerp(0f, -TalkRotationMaxAngle, loudness * 10f);
            }
        }
        else
        {
            _talkingAudioSource = null;
        }

        // Appliquer la rotation avec lerp smooth
        _headTopTransform.localRotation = Quaternion.Slerp(
            _headTopTransform.localRotation,
            Quaternion.Euler(targetRotation, 0f, 0f),
            Time.deltaTime * 20f);
    }

    private void FindHeadTransform()
    {
        // Chercher ANIM HEAD TOP dans la hiérarchie
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "code_head_top")
            {
                _headTopTransform = child;
                return;
            }
        }

        LOG.Warning("ANIM HEAD TOP not found for talking animation");
    }

    private void FindEyeTransforms()
    {
        try
        {
            Transform eyeLeft = null;
            Transform eyeRight = null;

            // Chercher ANIM EYE LEFT et ANIM EYE RIGHT sur le droid
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                switch (child.name)
                {
                    case "ANIM EYE LEFT":
                        eyeLeft = child;
                        break;
                    case "ANIM EYE RIGHT":
                        eyeRight = child;
                        break;
                }

                if (eyeLeft != null && eyeRight != null) break;
            }

            if (eyeLeft == null || eyeRight == null)
            {
                LOG.Warning("ANIM EYE LEFT/RIGHT not found for angry eyes effect");
                return;
            }

            // Trouver un PlayerAvatar pour copier la structure des paupières
            var playerSource = FindEyelidSource();

            if (playerSource == null)
            {
                LOG.Warning("No PlayerAvatar found to copy eyelid structure");
                return;
            }

            // Trouver code_eye_left et code_eye_right sur le droid (où attacher les paupières)
            Transform codeEyeLeft = null;
            Transform codeEyeRight = null;
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                switch (child.name)
                {
                    case "code_eye_left":
                        codeEyeLeft = child;
                        break;
                    case "code_eye_right":
                        codeEyeRight = child;
                        break;
                }

                if (codeEyeLeft != null && codeEyeRight != null) break;
            }

            // Fallback sur ANIM EYE si code_eye non trouvé
            var eyelidParentLeft = codeEyeLeft ?? eyeLeft;
            var eyelidParentRight = codeEyeRight ?? eyeRight;

            // Setup pour les deux yeux
            _eyelidsLeft = SetupEyeFromPlayer(eyelidParentLeft, playerSource, "LEFT",
                out _leftUpperEyelidRotationX,
                out _leftUpperEyelidRotationZ,
                out _leftLowerEyelidRotationX,
                out _leftLowerEyelidRotationZ);

            _eyelidsRight = SetupEyeFromPlayer(eyelidParentRight, playerSource, "RIGHT",
                out _rightUpperEyelidRotationX,
                out _rightUpperEyelidRotationZ,
                out _rightLowerEyelidRotationX,
                out _rightLowerEyelidRotationZ);

            // Désactiver par défaut (seront activées quand angry)
            if (_eyelidsLeft != null) _eyelidsLeft.SetActive(false);
            if (_eyelidsRight != null) _eyelidsRight.SetActive(false);

            LOG.Info($"Angry eyes setup: left={_eyelidsLeft != null}, right={_eyelidsRight != null}");
        }
        catch (Exception ex)
        {
            LOG.Error($"FindEyeTransforms: Exception - {ex.Message}\n{ex.StackTrace}");
        }
    }

    private Material GetDroidBodyMaterial()
    {
        // Chercher un renderer sur le corps du droid (pas les yeux)
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            var name = renderer.gameObject.name.ToLower();
            // Éviter les yeux et pupilles
            if (name.Contains("eye") || name.Contains("pupil") || name.Contains("eyelid")) continue;
            if (renderer.material != null)
            {
                return renderer.material;
            }
        }

        return null;
    }

    private Transform FindEyelidSource()
    {
        // Chercher PlayerAvatarVisuals dans la scène (c'est là que sont les paupières)
        var playerVisuals = FindObjectsOfType<PlayerAvatarVisuals>();

        foreach (var visuals in playerVisuals)
        {
            // Chercher EYELIDS LEFT pour vérifier que c'est une source valide
            foreach (var child in visuals.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "EYELIDS LEFT")
                {
                    return visuals.transform;
                }
            }
        }

        LOG.Warning("No PlayerAvatarVisuals with EYELIDS LEFT found");
        return null;
    }

    /// <summary>
    ///     Configure les paupières pour un oeil du droid en copiant depuis le player.
    /// </summary>
    private GameObject SetupEyeFromPlayer(
        Transform droidEye,
        Transform playerSource,
        string side,
        out Transform upperRotationX,
        out Transform upperRotationZ,
        out Transform lowerRotationX,
        out Transform lowerRotationZ)
    {
        upperRotationX = null;
        upperRotationZ = null;
        lowerRotationX = null;
        lowerRotationZ = null;

        var eyelidsName = $"EYELIDS {side}";

        // Trouver EYELIDS sur le player source
        Transform playerEyelids = null;
        foreach (var child in playerSource.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == eyelidsName)
            {
                playerEyelids = child;
                break;
            }
        }

        if (playerEyelids == null)
        {
            LOG.Warning($"{eyelidsName} not found on player source");
            return null;
        }

        // Vérifier si EYELIDS existe déjà sur le droid
        Transform droidEyelids = null;
        foreach (Transform child in droidEye)
        {
            if (child.name == eyelidsName)
            {
                droidEyelids = child;
                break;
            }
        }

        if (droidEyelids == null)
        {
            // Copier toute la hiérarchie EYELIDS du player vers le droid
            droidEyelids = CopyTransformHierarchy(playerEyelids, droidEye);

            // Agrandir les paupières pour s'adapter aux yeux du droid
            droidEyelids.localScale = Vector3.one * 1.3f;

            // Appliquer le material du droid aux paupières
            var droidBodyMaterial = GetDroidBodyMaterial();
            if (droidBodyMaterial != null)
            {
                foreach (var r in droidEyelids.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.material = droidBodyMaterial;
                }
            }
        }

        // Trouver les transforms de rotation dans la hiérarchie
        foreach (var child in droidEyelids.GetComponentsInChildren<Transform>(true))
        {
            switch (child.name)
            {
                case "eyelid_upper":
                    upperRotationX = child;
                    break;
                case "eyelid_upper_rotation":
                    upperRotationZ = child;
                    break;
                case "eyelid_lower":
                    lowerRotationX = child;
                    break;
                case "eyelid_lower_rotation":
                    lowerRotationZ = child;
                    break;
            }
        }

        return droidEyelids.gameObject;
    }

    private Transform CopyTransformHierarchy(Transform source, Transform newParent)
    {
        // Créer un nouveau GameObject avec le même nom
        var copy = new GameObject(source.name);
        copy.transform.SetParent(newParent, false);
        copy.transform.localPosition = source.localPosition;
        copy.transform.localRotation = source.localRotation;
        copy.transform.localScale = source.localScale;

        // Copier les MeshFilter et MeshRenderer si présents
        var sourceMeshFilter = source.GetComponent<MeshFilter>();
        var sourceMeshRenderer = source.GetComponent<MeshRenderer>();

        if (sourceMeshFilter != null && sourceMeshRenderer != null)
        {
            var copyMeshFilter = copy.AddComponent<MeshFilter>();
            var copyMeshRenderer = copy.AddComponent<MeshRenderer>();

            copyMeshFilter.sharedMesh = sourceMeshFilter.sharedMesh;
            copyMeshRenderer.sharedMaterials = sourceMeshRenderer.sharedMaterials;
            copyMeshRenderer.shadowCastingMode = sourceMeshRenderer.shadowCastingMode;
            copyMeshRenderer.receiveShadows = sourceMeshRenderer.receiveShadows;
        }

        // Récursivement copier les enfants
        foreach (Transform child in source)
        {
            CopyTransformHierarchy(child, copy.transform);
        }

        return copy.transform;
    }

    /// <summary>
    ///     Appelé par les animation events pour jouer un son de pas.
    /// </summary>
    public void PlayMediumFootstep()
    {
        if (ControllerTransform == null) return;
        // LOG.Debug($"PlayMediumFootstep -> calling droid at {ControllerTransform.position}");
        // Position légèrement au-dessus du sol pour que le raycast trouve le matériau
        var footPosition = ControllerTransform.position + Vector3.up * 0.1f;
        Materials.Instance.Impulse(
            footPosition,
            Vector3.down,
            Materials.SoundType.Medium,
            true,
            true,
            _materialTrigger,
            Materials.HostType.OtherPlayer);
    }

    private void CreateNameplate()
    {
        if (SourcePlayer == null || WorldSpaceUIParent.instance == null) return;

        var prefab = WorldSpaceUIParent.instance.playerNamePrefab;
        if (prefab == null) return;

        var nameplateGO = Instantiate(prefab, WorldSpaceUIParent.instance.transform);
        _nameplate = nameplateGO.GetComponent<WorldSpaceUIPlayerName>();

        if (_nameplate != null)
        {
            // Assigner le SourcePlayer pour éviter la destruction automatique
            _nameplate.playerAvatar = SourcePlayer;
            _nameplate.text.text = SourcePlayer.playerName;
        }
    }

    private void UpdateNameplate()
    {
        if (_nameplate == null || ControllerTransform == null || Camera.main == null) return;

        var worldPos = ControllerTransform.position + Vector3.up * NameplateHeight;
        var cameraPos = Camera.main.transform.position;
        var distance = Vector3.Distance(worldPos, cameraPos);

        // Vérifier si le droid est visible (pas de mur entre la caméra et le droid)
        var direction = worldPos - cameraPos;
        var isVisible = !Physics.Raycast(cameraPos, direction.normalized, distance,
            LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);

        // Alpha basé sur la distance (1 proche, 0 à 30m+)
        const float maxDistance = 8f;
        var distanceAlpha = Mathf.Clamp01(1f - distance / maxDistance);
        var targetAlpha = isVisible ? distanceAlpha : 0f;

        // Lerp smooth
        var currentColor = _nameplate.text.color;
        var newAlpha = Mathf.Lerp(currentColor.a, targetAlpha, Time.deltaTime * 5f);
        _nameplate.text.color = new Color(1f, 1f, 1f, newAlpha);

        // Mettre à jour la position
        var screenPos = SemiFunc.UIWorldToCanvasPosition(worldPos);
        var rect = _nameplate.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition = screenPos;
        }

        // Ajuster la taille selon la distance
        _nameplate.text.fontSize = Mathf.Clamp(20f - distance, 8f, 20f);
    }

    private void ApplyPlayerColor()
    {
        if (SourcePlayer == null) return;

        var colorIndex = StatsManager.instance.GetPlayerColor(SourcePlayer.steamID);
        if (colorIndex < 0 || colorIndex >= AssetManager.instance.playerColors.Count) return;

        var color = AssetManager.instance.playerColors[colorIndex];

        // Trouver le Cube qui contient les visuels
        var cube = transform.Find("Rigidbody/Cube") ?? transform.Find("Cube");
        if (cube == null)
        {
            foreach (var child in GetComponentsInChildren<Transform>())
            {
                if (child.name == "Cube")
                {
                    cube = child;
                    break;
                }
            }
        }

        if (cube == null)
        {
            LOG.Warning("Cube transform not found for color application");
            return;
        }

        // Appliquer la couleur à tous les renderers (sauf yeux, pupilles et health shadow)
        foreach (var renderer in cube.GetComponentsInChildren<Renderer>())
        {
            var name = renderer.gameObject.name;
            if (name.Contains("eye") || name.Contains("pupil") || name.Contains("mesh_health"))
            {
                continue;
            }

            foreach (var material in renderer.materials)
            {
                material.SetColor("_AlbedoColor", color);
            }
        }
    }

    private void InitializeFSM()
    {
        fsm = new StateMachine(this, DefaultState);
        fsm.AddState(StateId.Idle, new IdleState());
        fsm.AddState(StateId.Wander, new WanderState());
        fsm.AddState(StateId.Sprint, new SprintState());
        fsm.AddState(StateId.CheckMap, new CheckMapState());
    }

    private void FindCriticalTransforms()
    {
        ControllerTransform = transform.Find("Controller");
        if (ControllerTransform == null)
        {
            foreach (var child in GetComponentsInChildren<Transform>())
            {
                if (child.name == "Controller")
                {
                    ControllerTransform = child;
                    break;
                }
            }
        }

        if (ControllerTransform == null)
        {
            LOG.Warning("Controller transform not found, creating one");
            var controllerGO = new GameObject("Controller");
            controllerGO.transform.SetParent(transform);
            controllerGO.transform.localPosition = Vector3.zero;
            ControllerTransform = controllerGO.transform;
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

        if (ControllerTransform != null)
        {
            existingAgent = ControllerTransform.GetComponent<NavMeshAgent>();
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

            // Ajouter le relais pour les animation events (footsteps, etc.)
            _animator.gameObject.AddComponent<HallucinationAnimEvents>();
        }
        else
        {
            LOG.Warning("No Animator found on prefab");
        }
    }

    private void SetupNavigation()
    {
        if (ControllerTransform == null)
        {
            LOG.Error("Cannot setup navigation: Controller is null");
            return;
        }

        _navAgent = ControllerTransform.GetComponent<NavMeshAgent>();
        if (_navAgent == null)
        {
            _navAgent = ControllerTransform.gameObject.AddComponent<NavMeshAgent>();
        }

        var startPos = ControllerTransform.position;
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

        ControllerTransform.position = validPosition;
        _navAgent.Warp(validPosition);
        _navAgent.enabled = true;

        SetupCharacterController();
    }

    private void SetupCharacterController()
    {
        if (ControllerTransform == null) return;

        var existingCC = ControllerTransform.GetComponent<CharacterController>();
        if (existingCC != null)
        {
            DestroyImmediate(existingCC);
        }

        _charController = ControllerTransform.gameObject.AddComponent<CharacterController>();
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
        Sprint,
        CheckMap
    }

    private class IdleState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const string ClipName = "LostDroidStand";
        private const float IdleLoopStart = 0f;
        private const float IdleLoopEnd = 4.5f;
        private const float BlendDelay = 0.3f; // Temps pour laisser l'Animator faire le blend

        private const float MinIdleTime = 10f;
        private const float MaxIdleTime = 15f;
        private const float PrecomputeRetryInterval = 1f;
        private const float CheckMapChance = 0.15f;

        private Animator _animator;
        private bool _animatorTakenOver;
        private AnimationClip _clip;
        private float _currentTime;

        private float _duration;
        private float _precomputeTimer;
        private GameObject _targetObject;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _duration = Random.Range(MinIdleTime, MaxIdleTime);
            _precomputeTimer = 0f;
            _animatorTakenOver = false;
            Machine.Owner.ResetPath();
            Machine.Owner.HasChangedMovementState = false;

            // Setup animation (mais on ne désactive pas l'Animator tout de suite)
            _animator = Machine.Owner._animator;
            if (_animator != null)
            {
                // Trouver le clip
                _clip = null;
                foreach (var clip in _animator.runtimeAnimatorController.animationClips)
                {
                    if (clip.name == ClipName)
                    {
                        _clip = clip;
                        break;
                    }
                }

                if (_clip != null)
                {
                    _targetObject = _animator.gameObject;
                }
            }

            // Démarrer le pré-calcul immédiatement
            Machine.Owner.StartPrecomputeDestination();
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            // Attendre le blend avant de prendre le contrôle
            if (!_animatorTakenOver && _clip != null && _targetObject != null)
            {
                if (TimeElapsed >= BlendDelay)
                {
                    _animator.enabled = false;
                    _currentTime = IdleLoopStart;
                    _animatorTakenOver = true;
                }
            }

            // Jouer l'animation idle en boucle (seulement après avoir pris le contrôle)
            if (_animatorTakenOver && _clip != null && _targetObject != null)
            {
                _currentTime += Time.deltaTime;
                if (_currentTime >= IdleLoopEnd)
                {
                    _currentTime = IdleLoopStart;
                }

                _clip.SampleAnimation(_targetObject, _currentTime);
            }

            // Réessayer le pré-calcul si pas encore de destination
            if (!Machine.Owner.HasPrecomputedDestination)
            {
                _precomputeTimer -= Time.deltaTime;
                if (_precomputeTimer <= 0f)
                {
                    Machine.Owner.StartPrecomputeDestination();
                    _precomputeTimer = PrecomputeRetryInterval;
                }
            }

            if (TimeElapsed >= _duration)
            {
                // Choisir le prochain état
                var roll = Random.value;
                if (roll < CheckMapChance)
                {
                    Machine.NextStateStateId = StateId.CheckMap;
                }
                else if (roll < CheckMapChance + SprintChance)
                {
                    Machine.NextStateStateId = StateId.Sprint;
                }
                else
                {
                    Machine.NextStateStateId = StateId.Wander;
                }
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);

            // Réactiver l'Animator sauf si on va vers CheckMap (qui gère son propre contrôle)
            if (_animator != null && next != StateId.CheckMap)
            {
                _animator.enabled = true;
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

            // Si on vient d'un autre état de mouvement (Wander ↔ Sprint), garder la même destination
            var isMovementSwitch = previous == StateId.Wander || previous == StateId.Sprint;
            if (!isMovementSwitch)
            {
                // Nouvelle destination nécessaire (depuis Idle)
                if (!Machine.Owner.TrySetRandomDestination())
                {
                    Machine.NextStateStateId = StateId.Idle;
                    return;
                }
            }

            // Juste changer la vitesse
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

        protected override void SetMovementFlag(bool value)
        {
            Machine.Owner.IsWalking = value;
        }
    }

    private class SprintState : MovementState
    {
        protected override float MinDuration => 3f;
        protected override float MaxDuration => 8f;
        protected override float Speed => SprintSpeed;
        protected override StateId SwitchTargetState => StateId.Wander;

        protected override void SetMovementFlag(bool value)
        {
            Machine.Owner.IsSprinting = value;
        }
    }

    private class CheckMapState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const string ClipName = "LostDroidStand";

        // Timestamps en secondes (basés sur les keyframes de l'animation)
        private const float RaiseStartTime = 4.65f;
        private const float LookStartTime = 5.5f;
        private const float LookEndTime = 6.9333334f;
        private const float LowerEndTime = 7.15f;

        // Durée de la phase de regard (configurable)
        private const float MinLookDuration = 3f;
        private const float MaxLookDuration = 8f;

        private Animator _animator;
        private AnimationClip _clip;

        private Phase _currentPhase;
        private float _currentTime;
        private float _lookDuration;
        private float _lookTimer;
        private GameObject _targetObject;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);

            _animator = Machine.Owner._animator;
            if (_animator == null)
            {
                LOG.Warning("CheckMapState: Animator is null!");
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            // Trouver le clip
            _clip = null;
            foreach (var clip in _animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name == ClipName)
                {
                    _clip = clip;
                    break;
                }
            }

            if (_clip == null)
            {
                LOG.Warning($"CheckMapState: Clip '{ClipName}' not found!");
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            _targetObject = _animator.gameObject;

            // Désactiver les flags de mouvement
            Machine.Owner.IsWalking = false;
            Machine.Owner.IsSprinting = false;
            Machine.Owner.ResetPath();

            // Désactiver l'Animator pour prendre le contrôle direct
            _animator.enabled = false;

            // Configurer la durée de regard
            _lookDuration = Random.Range(MinLookDuration, MaxLookDuration);
            _lookTimer = 0f;

            // Démarrer la phase de lever
            _currentPhase = Phase.Raise;
            _currentTime = RaiseStartTime;

            LOG.Debug($"CheckMapState: ENTER - Clip length={_clip.length}s, starting at {_currentTime}s");
            LOG.Debug(
                $"CheckMapState: Thresholds - Raise={RaiseStartTime}s, Look={LookStartTime}s-{LookEndTime}s, Lower={LowerEndTime}s");
            LOG.Debug($"CheckMapState: LookDuration={_lookDuration:F1}s");

            // Appliquer la première frame
            _clip.SampleAnimation(_targetObject, _currentTime);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (_clip == null || _targetObject == null)
            {
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            // Avancer le temps
            _currentTime += Time.deltaTime;

            switch (_currentPhase)
            {
                case Phase.Raise:
                    if (_currentTime >= LookStartTime)
                    {
                        LOG.Debug("CheckMapState: Transitioning to Phase.Look");
                        _currentPhase = Phase.Look;
                        _lookTimer = 0f;
                    }

                    break;

                case Phase.Look:
                    _lookTimer += Time.deltaTime;

                    if (_currentTime >= LookEndTime)
                    {
                        if (_lookTimer < _lookDuration)
                        {
                            // Boucler
                            _currentTime = LookStartTime;
                        }
                        else
                        {
                            LOG.Debug("CheckMapState: Transitioning to Phase.Lower");
                            _currentPhase = Phase.Lower;
                        }
                    }

                    break;

                case Phase.Lower:
                    if (_currentTime >= LowerEndTime)
                    {
                        LOG.Debug("CheckMapState: Animation complete, returning to Idle");
                        Machine.NextStateStateId = StateId.Idle;
                        return;
                    }

                    break;
            }

            // Appliquer l'animation
            _clip.SampleAnimation(_targetObject, _currentTime);
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            LOG.Debug($"CheckMapState: EXIT -> {next}");

            // Réactiver l'Animator
            if (_animator != null)
            {
                _animator.enabled = true;
            }
        }

        private enum Phase
        {
            Raise,
            Look,
            Lower
        }
    }

    #endregion
}