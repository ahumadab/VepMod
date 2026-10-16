// PROTOTYPE for #65, throwaway: answers whether the game's PlayerAvatarVisuals can animate a Hallucination.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using VepMod.Enemies.Whispral.Visuals;
using VepMod.VepFramework;
using Object = UnityEngine.Object;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Puts the game's PlayerAvatarVisuals on a Hallucination's Cube, fed every frame by a fake PlayerAvatar that sits
///     on a never-activated GameObject, so Unity never calls its Awake, Start, Update or OnDestroy.
/// </summary>
internal sealed class PrototypePlayerVisuals : MonoBehaviour
{
    private const float LogInterval = 1f; // s

    internal static readonly VepLogger LOG = VepLogger.Create<PrototypePlayerVisuals>();

    // ponytail: never emptied, so a lifecycle call on a destroyed fake is still caught; a prototype leaks a few refs.
    internal static readonly HashSet<PlayerAvatar> Fakes = new();
    private static int _footstepsBlocked;

    private static readonly int MovingKey = Animator.StringToHash("Moving");
    private static readonly int SprintingKey = Animator.StringToHash("Sprinting");
    private static readonly int TurningKey = Animator.StringToHash("Turning");
    private static readonly int FallingKey = Animator.StringToHash("Falling");
    private static readonly int GrabbingKey = Animator.StringToHash("Grabbing");

    private Animator _animator;
    private Transform _cube;
    private PlayerAvatar _fake;
    private PlayerEyes _fakeEyes;
    private Hallucination _hallucination;
    private PlayerAvatar _imitated;
    private float _logTimer;
    private PlayerAvatarVisuals _pav;

    public static void Enable(Hallucination hallucination)
    {
        if (hallucination.GetComponent<PrototypePlayerVisuals>() != null)
        {
            LOG.Warning($"PROTO {hallucination.name} already runs the game's PlayerAvatarVisuals");
            return;
        }

        if (!GameManager.Multiplayer())
        {
            LOG.Warning("PROTO refused in singleplayer: PlayerAvatarVisuals would take the local path and hide the body");
            return;
        }

        hallucination.gameObject.AddComponent<PrototypePlayerVisuals>().Switch(hallucination);
    }

    internal static void OnFakeCall(string method)
    {
        if (method == nameof(PlayerAvatar.Footstep))
        {
            _footstepsBlocked++;
            LOG.Debug($"PROTO blocked PlayerAvatar.Footstep on the fake ({_footstepsBlocked} so far)");
        }
        else
        {
            LOG.Warning($"PROTO fake PlayerAvatar.{method} called, original skipped");
        }
    }

    private void Switch(Hallucination hallucination)
    {
        _hallucination = hallucination;
        _imitated = hallucination.ImitatedPlayer;
        var source = _imitated != null ? _imitated.playerAvatarVisuals : null;
        var cube = HallucinationVisualsClone.FindCube(hallucination.transform);
        var animator = cube != null ? cube.GetComponent<Animator>() : null;
        if (source == null || cube == null || animator == null)
        {
            LOG.Warning($"PROTO cannot switch {hallucination.name}: source visuals {source != null}, " +
                        $"cube {cube != null}, animator {animator != null}");
            Destroy(this);
            return;
        }

        _cube = cube;
        _animator = animator;
        var playerList = GameDirector.instance.PlayerList;
        var playerCountBefore = playerList.Count;

        _fake = CreateFake(hallucination.transform, _imitated!, source, out _fakeEyes);

        var controller = hallucination.ControllerTransform;
        var parentBefore = cube.parent != null ? cube.parent.name : "none";
        var offsetBefore = cube.position - controller.position;
        // PlayerAvatarVisuals writes the Cube's world position and rotation; under the root, which never moves, no
        // parent move adds to it after its Update.
        cube.SetParent(hallucination.transform, true);

        _pav = cube.gameObject.AddComponent<PlayerAvatarVisuals>();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), _pav);
        RemapToClone(_pav, source.transform, cube);

        _pav.isMenuAvatar = false;
        _pav.currentPose = PlayerAvatarVisuals.Pose.Stand;
        _pav.playerAvatar = _fake;
        _pav.playerCosmetics = _fake.playerCosmetics;
        _pav.playerEyes = _fakeEyes;
        // Start overwrites it from the Cube's children, which have none: the Start postfix puts it back.
        _pav.playerAvatarRightArm = _fake.GetComponent<PlayerAvatarRightArm>();
        _pav.animator = animator;
        _fake.playerAvatarVisuals = _pav;
        _fake.PlayerVisionTarget.VisionTransform = _pav.headLookAtTransform;

        Feed();
        _pav.visualPosition = _fake.clientPositionCurrent;

        hallucination.BodyPose.enabled = false;
        hallucination.PrototypeGameVisuals = true;

        LOG.Debug($"PROTO switch {hallucination.name}: Cube parent {parentBefore} -> {cube.parent!.name}, " +
                  $"Cube-controller offset was {offsetBefore}, feed offset is (0, {HallucinationMovement.VisualYOffset}, 0); " +
                  "feed in LateUpdate, read by PlayerAvatarVisuals.Update the next frame; HallucinationBodyPose disabled");
        LogReferences(hallucination.transform);
        LOG.Debug($"PROTO switch fake: activeSelf={_fake.gameObject.activeSelf}, " +
                  $"activeInHierarchy={_fake.gameObject.activeInHierarchy}, isLocal={_fake.isLocal}, " +
                  $"steamID={_fake.steamID}, photonView.IsMine={_fake.photonView.IsMine}, " +
                  $"PlayerList {playerCountBefore} -> {playerList.Count}, fake in PlayerList={playerList.Contains(_fake)}, " +
                  $"imitated {_imitated!.playerName} in PlayerList={playerList.Contains(_imitated)}");
    }

    private static PlayerAvatar CreateFake(Transform parent, PlayerAvatar imitated, PlayerAvatarVisuals source,
        out PlayerEyes eyes)
    {
        var fakeObject = new GameObject("PROTOTYPE Fake PlayerAvatar");
        // Before any AddComponent: on an inactive GameObject Unity defers Awake, and never calls it if the object is
        // never activated.
        fakeObject.SetActive(false);
        fakeObject.transform.SetParent(parent, false);

        var fake = fakeObject.AddComponent<PlayerAvatar>();
        Fakes.Add(fake);
        fake.photonView = fakeObject.AddComponent<PhotonView>();
        fake.playerHealth = fakeObject.AddComponent<PlayerHealth>();
        fake.PlayerVisionTarget = fakeObject.AddComponent<PlayerVisionTarget>();
        fake.localCamera = fakeObject.AddComponent<PlayerLocalCamera>();
        fake.localCamera.playerAvatar = fake;
        fake.mapToolController = fakeObject.AddComponent<MapToolController>();
        fake.playerCosmetics = fakeObject.AddComponent<PlayerCosmetics>();
        fake.isLocal = false;
        fake.steamID = imitated.steamID;
        fake.playerName = imitated.playerName;

        eyes = fakeObject.AddComponent<PlayerEyes>();

        // AnimationLogic sets 'Grabbing' while poseNew equals grabberPose: they start apart, as on a real player.
        var arm = fakeObject.AddComponent<PlayerAvatarRightArm>();
        arm.mapToolController = fake.mapToolController;
        var sourceArm = source.GetComponent<PlayerAvatarRightArm>();
        arm.grabberPose = sourceArm != null ? sourceArm.grabberPose : Vector3.one * 999f;
        arm.poseNew = sourceArm != null ? sourceArm.basePose : Vector3.zero;
        return fake;
    }

    private static IEnumerable<FieldInfo> ObjectFields()
    {
        return typeof(PlayerAvatarVisuals)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => typeof(Object).IsAssignableFrom(field.FieldType) ||
                            (field.FieldType.IsArray &&
                             typeof(Object).IsAssignableFrom(field.FieldType.GetElementType())));
    }

    // FromJsonOverwrite keeps the Imitated Player's references: each one moves to the object at the same path under
    // the Cube, which stands for the player's 'Player Visuals'. Paths, not names: two fields point at a 'Mesh Flat'.
    private static void RemapToClone(PlayerAvatarVisuals pav, Transform sourceRoot, Transform cloneRoot)
    {
        foreach (var field in ObjectFields())
        {
            if (field.FieldType.IsArray)
            {
                if (field.GetValue(pav) is not Array array) continue;
                var remapped = Array.CreateInstance(field.FieldType.GetElementType()!, array.Length);
                for (var i = 0; i < array.Length; i++)
                {
                    remapped.SetValue(Remap((Object)array.GetValue(i), sourceRoot, cloneRoot, out var verdict), i);
                    LOG.Debug($"PROTO ref {field.Name}[{i}]: {verdict}");
                }

                field.SetValue(pav, remapped);
                continue;
            }

            var value = (Object)field.GetValue(pav);
            if (value == null) continue;
            field.SetValue(pav, Remap(value, sourceRoot, cloneRoot, out var fieldVerdict));
            LOG.Debug($"PROTO ref {field.Name}: {fieldVerdict}");
        }
    }

    private static Object? Remap(Object? value, Transform sourceRoot, Transform cloneRoot, out string verdict)
    {
        var transform = value switch
        {
            Component component => component.transform,
            GameObject gameObject => gameObject.transform,
            _ => null
        };
        if (value == null || transform == null)
        {
            verdict = "null or not a scene object, kept";
            return value;
        }

        if (!transform.gameObject.scene.IsValid())
        {
            verdict = $"asset {value.name}, kept";
            return value;
        }

        if (!transform.IsChildOf(sourceRoot))
        {
            verdict = $"{value.name} outside the Imitated Player's visuals, cleared";
            return null;
        }

        var path = PathUnder(sourceRoot, transform);
        var target = path.Length == 0 ? cloneRoot : cloneRoot.Find(path);
        if (target == null)
        {
            verdict = $"'{path}' missing under the Cube, cleared";
            return null;
        }

        Object? result = value switch
        {
            GameObject => target.gameObject,
            Transform => target,
            _ => target.GetComponent(value.GetType())
        };
        verdict = result != null
            ? $"-> Cube/{path} ({value.GetType().Name})"
            : $"Cube/{path} has no {value.GetType().Name}, cleared";
        return result;
    }

    private static string PathUnder(Transform root, Transform transform)
    {
        var parts = new List<string>();
        for (var t = transform; t != root; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private void LogReferences(Transform root)
    {
        var outside = 0;
        foreach (var field in ObjectFields())
        {
            var values = field.FieldType.IsArray
                ? (field.GetValue(_pav) as Array)?.Cast<Object>().ToArray() ?? Array.Empty<Object>()
                : new[] { (Object)field.GetValue(_pav) };
            foreach (var value in values)
            {
                var transform = value switch
                {
                    Component component => component.transform,
                    GameObject gameObject => gameObject.transform,
                    _ => null
                };
                var where = value == null ? "null"
                    : transform == null || !transform.gameObject.scene.IsValid() ? $"{value.name} (asset)"
                    : transform.IsChildOf(root) ? $"{value.name} (in the Hallucination)"
                    : $"{value.name} OUTSIDE the Hallucination";
                if (where.EndsWith("OUTSIDE the Hallucination")) outside++;
                LOG.Debug($"PROTO set {field.Name} = {where}");
            }
        }

        LOG.Debug($"PROTO refs outside the Hallucination: {outside}");
    }

    // After every Update of the frame, the Hallucination's movement included: PlayerAvatarVisuals reads it in its
    // next Update, one frame late whatever the order of the two scripts' Updates.
    private void LateUpdate()
    {
        if (_fake == null) return;

        Feed();

        _logTimer -= Time.deltaTime;
        if (_logTimer > 0f) return;
        _logTimer = LogInterval;
        LogTick();
    }

    private void Feed()
    {
        var controller = _hallucination.ControllerTransform;
        var movement = _hallucination.Movement;
        var pose = _hallucination.BodyPose;

        var position = controller.position + Vector3.up * HallucinationMovement.VisualYOffset;
        _fake.clientPosition = _fake.clientPositionCurrent = position;
        _fake.clientRotation = _fake.clientRotationCurrent = controller.rotation;

        var velocity = movement.CurrentVelocity;
        velocity.y = 0f;
        var grounded = movement.DebugIsGrounded;
        _fake.rbVelocity = controller.InverseTransformDirection(velocity);
        _fake.rbVelocityRaw = new Vector3(velocity.x, grounded ? 0f : movement.DebugVerticalVelocity, velocity.z);
        _fake.isMoving = pose.PrototypeGait != Gait.Still;
        _fake.isSprinting = pose.PrototypeGait == Gait.Sprint;
        _fake.isGrounded = grounded;
        _fake.mapToolController.Active = pose.PrototypeCheckingMap;

        // The fake's camera is its own transform (PlayerLocalCamera.GetOverrideTransform on a non-local avatar): its
        // pitch tilts the head, its forward centres the head look-at cone.
        var head = _pav != null ? _pav.headLookAtTransform : null;
        var victimCamera = Camera.main;
        var staring = pose.PrototypeStaring && victimCamera != null && head != null;
        _fakeEyes.lookAtActive = staring;
        _fakeEyes.lookAt = staring ? victimCamera!.transform : null;
        var cameraTransform = _fake.localCamera.transform;
        cameraTransform.position = head != null ? head.position : position;
        cameraTransform.rotation = staring
            ? Quaternion.LookRotation(victimCamera!.transform.position - head!.position)
            : controller.rotation;
    }

    private void LogTick()
    {
        var localPath = !GameManager.Multiplayer() || _fake.photonView.IsMine;
        var playerList = GameDirector.instance.PlayerList;
        var expected = _fake.clientPositionCurrent;
        LOG.Debug($"PROTO tick {_hallucination.name} [{_hallucination.DebugStateName}]: " +
                  $"pavLocalPath={localPath}, lean={Euler(_pav.leanTransform)}, tilt={Euler(_pav.tiltTransform)}, " +
                  $"legTwist={Euler(_pav.legTwistTransform)}, headSide={Euler(_pav.headSideTransform)}, " +
                  $"bodyTopSide={Euler(_pav.bodyTopSideTransform)}, headUp={Euler(_pav.headUpTransform)}, " +
                  $"headLookAt={Euler(_pav.headLookAtTransform)}, " +
                  $"bodyLag={Quaternion.Angle(_cube.rotation, _hallucination.ControllerTransform.rotation):F1}°, " +
                  $"cubeToFeed={Vector3.Distance(_cube.position, expected):F2}m | " +
                  $"anim Moving={_animator.GetBool(MovingKey)} Sprinting={_animator.GetBool(SprintingKey)} " +
                  $"Turning={_animator.GetBool(TurningKey)} Falling={_animator.GetBool(FallingKey)} " +
                  $"Grabbing={_animator.GetBool(GrabbingKey)} speed={_animator.speed:F2} | " +
                  $"fake isMoving={_fake.isMoving} isSprinting={_fake.isSprinting} isGrounded={_fake.isGrounded} " +
                  $"vel={_fake.rbVelocityRaw.magnitude:F2}m/s staring={_fakeEyes.lookAtActive} | " +
                  $"footstepsBlocked={_footstepsBlocked} | fake activeInHierarchy={_fake.gameObject.activeInHierarchy} " +
                  $"inPlayerList={playerList.Contains(_fake)} PlayerList={playerList.Count}");
    }

    private static string Euler(Transform? transform)
    {
        if (transform == null) return "none";
        var e = transform.localEulerAngles;
        return $"({Mathf.DeltaAngle(0f, e.x):F1}, {Mathf.DeltaAngle(0f, e.y):F1}, {Mathf.DeltaAngle(0f, e.z):F1})";
    }

    private void OnDestroy()
    {
        if (_fake == null && _imitated == null) return;

        var playerList = GameDirector.instance != null ? GameDirector.instance.PlayerList : null;
        LOG.Debug($"PROTO despawn {name}: PlayerList={playerList?.Count}, " +
                  $"imitated {(_imitated != null ? _imitated.playerName : "gone")} present=" +
                  $"{_imitated != null && playerList != null && playerList.Contains(_imitated)}, " +
                  $"footstepsBlocked={_footstepsBlocked}");
    }
}

/// <summary>
///     Skips every PlayerAvatar method that would act on the fake: its lifecycle, in case Unity ever ran it, and the
///     animation events PlayerAvatarVisuals forwards (footsteps alert enemies through EnemyDirector.SetInvestigate).
/// </summary>
[HarmonyPatch]
internal static class PrototypeFakePlayerAvatarPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return new[]
            {
                "Awake", "Start", "OnDestroy", nameof(PlayerAvatar.Footstep), nameof(PlayerAvatar.StandToCrouch),
                nameof(PlayerAvatar.CrouchToStand), nameof(PlayerAvatar.CrouchToCrawl),
                nameof(PlayerAvatar.CrawlToCrouch)
            }
            .Select(name => (MethodBase)AccessTools.Method(typeof(PlayerAvatar), name));
    }

    private static bool Prefix(PlayerAvatar __instance, MethodBase __originalMethod)
    {
        if (!PrototypePlayerVisuals.Fakes.Contains(__instance)) return true;
        PrototypePlayerVisuals.OnFakeCall(__originalMethod.Name);
        return false;
    }
}

[HarmonyPatch(typeof(PlayerAvatarVisuals), "Start")]
internal static class PrototypePlayerAvatarVisualsStartPatch
{
    private static void Postfix(PlayerAvatarVisuals __instance)
    {
        if (__instance.playerAvatar == null || !PrototypePlayerVisuals.Fakes.Contains(__instance.playerAvatar)) return;

        __instance.playerAvatarRightArm = __instance.playerAvatar.GetComponent<PlayerAvatarRightArm>();
        PrototypePlayerVisuals.LOG.Debug($"PROTO PlayerAvatarVisuals.Start on {__instance.name}: right arm put back " +
                                         $"to the fake's ({__instance.playerAvatarRightArm != null}), " +
                                         $"animator={__instance.animator != null}");
    }
}
#endif
