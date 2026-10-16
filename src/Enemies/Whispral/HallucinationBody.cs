using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Animations;
using VepMod.VepFramework;
using Object = UnityEngine.Object;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     A Hallucination's body, animated by the game's own PlayerAvatarVisuals on its remote-player path, as other players
///     see a teammate (ADR-0005): body spring, lean, sprint tilt, head steer and look, leg twist, and the locomotion
///     animator parameters. PlayerAvatarVisuals reads a fake PlayerAvatar, on a GameObject that is never activated, which
///     the Hallucination feeds every frame.
/// </summary>
internal sealed class HallucinationBody
{
    private const float LogInterval = 0.5f; // s

    // Far enough that the head's own motion does not change the direction it looks in.
    private const float GlancePointDistance = 10f; // m

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationBody>();

    private static readonly int MovingKey = Animator.StringToHash("Moving");
    private static readonly int SprintingKey = Animator.StringToHash("Sprinting");
    private static readonly int TurningKey = Animator.StringToHash("Turning");
    private static readonly int GrabbingKey = Animator.StringToHash("Grabbing");
    private static readonly int FallingKey = Animator.StringToHash("Falling");

    // Read by the PlayerAvatar guard, which runs for every player.
    private static readonly Dictionary<PlayerAvatar, HallucinationBody> Fakes = new();

    private readonly Animator _animator;
    private readonly Transform _body;
    private readonly PlayerAvatar _fake;
    private readonly PlayerEyes _fakeEyes;
    private readonly Transform _glancePoint;
    private readonly Hallucination _hallucination;
    private readonly PlayerAvatarVisuals _visuals;
    private int _footstepsBlocked;
    private float? _glanceYaw;
    private float _glanceStart;
    private float _glanceBodyYawFrom;
    private float _glanceWidestLook;
    private float _glanceWidestHead;
    private Gait _lastGait = Gait.Still;
    private float _logTimer;
    private bool _wasFalling;

    private HallucinationBody(Hallucination hallucination, PlayerAvatarVisuals source, Transform cube,
        Animator animator)
    {
        _hallucination = hallucination;
        _body = cube;
        _animator = animator;

        var playerList = GameDirector.instance.PlayerList;
        var playersBefore = playerList.Count;
        _fake = CreateFake(hallucination.transform, hallucination.ImitatedPlayer, source, out _fakeEyes);
        Fakes[_fake] = this;
        _glancePoint = new GameObject("Glance Point").transform;
        _glancePoint.SetParent(hallucination.transform, false);

        _visuals = cube.gameObject.AddComponent<PlayerAvatarVisuals>();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), _visuals);
        var cleared = RepointIntoClone(_visuals, source.transform, cube);
        _visuals.isMenuAvatar = false;
        _visuals.playerAvatar = _fake;
        _visuals.playerCosmetics = _fake.playerCosmetics;
        _visuals.playerEyes = _fakeEyes;
        _visuals.playerAvatarRightArm = _fake.GetComponent<PlayerAvatarRightArm>();
        _visuals.animator = animator;
        _fake.playerAvatarVisuals = _visuals;
        _fake.PlayerVisionTarget.VisionTransform = _visuals.headLookAtTransform;

        // Animate runs its Update right after the Hallucination moves: in Unity's order it would read the fake a frame
        // late half the time. Disabled, it never runs its Start either, which would overwrite playerAvatarRightArm.
        _visuals.enabled = false;

        // The prefab's Cube follows the Rigidbody through this constraint, which Unity applies after every Update and
        // would overwrite the body spring.
        var constraint = cube.GetComponent<ParentConstraint>();
        if (constraint != null) constraint.constraintActive = false;

        Feed();
        _visuals.visualPosition = _fake.clientPositionCurrent;

        var imitated = hallucination.ImitatedPlayer;
        LOG.Debug($"Body of {hallucination.name}: fake PlayerAvatar active={_fake.gameObject.activeSelf}, " +
                  $"isLocal={_fake.isLocal}, IsMine={_fake.photonView.IsMine}; PlayerList {playersBefore} -> " +
                  $"{playerList.Count}, fake in it={playerList.Contains(_fake)}, {imitated.playerName} in it=" +
                  $"{playerList.Contains(imitated)}; PlayerAvatarVisuals references outside the Hallucination: " +
                  $"{CountOutside(_visuals, hallucination.transform)}, cleared: {string.Join(", ", cleared)}");
    }

    private string Name => _hallucination.name;

    /// <returns>
    ///     Null in singleplayer, or when the Imitated Player has no visuals: the body then stays still and follows the
    ///     controller.
    /// </returns>
    public static HallucinationBody? Create(Hallucination hallucination, Transform cube, Animator animator)
    {
        if (!GameManager.Multiplayer())
        {
            LOG.Warning($"{hallucination.name}: in singleplayer the game's PlayerAvatarVisuals takes the local " +
                        "player's path, which hides the body, so this Hallucination's body is not animated. " +
                        "Test it with two instances.");
            return null;
        }

        var source = hallucination.ImitatedPlayer.playerAvatarVisuals;
        if (source == null)
        {
            LOG.Warning($"{hallucination.name}: the Imitated Player has no visuals, the body is not animated");
            return null;
        }

        return new HallucinationBody(hallucination, source, cube, animator);
    }

    /// <summary>
    ///     Called by the PlayerAvatar guard before each method it watches; counts the footsteps it blocks.
    /// </summary>
    /// <returns>Whether the avatar is a Hallucination's fake, whose original method must not run.</returns>
    public static bool BlocksCall(PlayerAvatar avatar, string method)
    {
        if (!Fakes.TryGetValue(avatar, out var body)) return false;

        if (method == nameof(PlayerAvatar.Footstep)) body._footstepsBlocked++;
        else LOG.Warning($"{body.Name}: fake PlayerAvatar.{method} called, original skipped");
        return true;
    }

    /// <summary>
    ///     Once a frame, after the Hallucination has moved.
    /// </summary>
    public void Animate()
    {
        Feed();
        _visuals.Update();

        if (!LOG.DebugEnabled) return;
        TrackGlance();
        // A fall lasts less than a periodic line's interval.
        var falling = _animator.GetBool(FallingKey);
        if (falling != _wasFalling)
        {
            _wasFalling = falling;
            LOG.Debug($"Body of {Name}: {(falling ? "falls" : "lands")}, {_hallucination.Movement.VerticalVelocity:F1} m/s");
        }


        _logTimer -= Time.deltaTime;
        if (_logTimer > 0f) return;
        _logTimer = LogInterval;
        LogPose();
    }

    public void Release()
    {
        Fakes.Remove(_fake);

        var playerList = GameDirector.instance != null ? GameDirector.instance.PlayerList : null;
        var imitated = _hallucination.ImitatedPlayer;
        LOG.Debug($"Body of {Name} gone after {_footstepsBlocked} footsteps blocked: PlayerList={playerList?.Count}, " +
                  $"Imitated Player {(imitated != null ? imitated.playerName : "gone")} in it=" +
                  $"{imitated != null && playerList != null && playerList.Contains(imitated)}");
    }

    private static PlayerAvatar CreateFake(Transform parent, PlayerAvatar imitated, PlayerAvatarVisuals source,
        out PlayerEyes eyes)
    {
        var fakeObject = new GameObject("Fake PlayerAvatar");
        // Before any AddComponent: on an inactive GameObject Unity defers Awake, and never runs it, nor Start, Update or
        // OnDestroy, if the object is never activated.
        fakeObject.SetActive(false);
        fakeObject.transform.SetParent(parent, false);

        var fake = fakeObject.AddComponent<PlayerAvatar>();
        fake.photonView = fakeObject.AddComponent<PhotonView>();
        fake.playerHealth = fakeObject.AddComponent<PlayerHealth>();
        fake.PlayerVisionTarget = fakeObject.AddComponent<PlayerVisionTarget>();
        fake.localCamera = fakeObject.AddComponent<PlayerLocalCamera>();
        fake.localCamera.playerAvatar = fake;
        fake.mapToolController = fakeObject.AddComponent<MapToolController>();
        fake.playerCosmetics = fakeObject.AddComponent<PlayerCosmetics>();
        fake.isLocal = false;
        // PlayerAvatarVisuals reads the Imitated Player's sprint upgrade through it.
        fake.steamID = imitated.steamID;
        fake.playerName = imitated.playerName;

        eyes = fakeObject.AddComponent<PlayerEyes>();

        // AnimationLogic raises the hand while poseNew equals grabberPose: they start apart, as on a real player.
        var arm = fakeObject.AddComponent<PlayerAvatarRightArm>();
        arm.mapToolController = fake.mapToolController;
        var sourceArm = source.GetComponent<PlayerAvatarRightArm>();
        arm.grabberPose = sourceArm != null ? sourceArm.grabberPose : Vector3.one * 999f;
        arm.poseNew = sourceArm != null ? sourceArm.basePose : Vector3.zero;
        return fake;
    }

    private void Feed()
    {
        var controller = _hallucination.ControllerTransform;
        var movement = _hallucination.Movement;
        var pose = _hallucination.BodyPose;

        // The target, not the controller's slerped rotation: the body spring smooths it.
        _fake.clientPositionCurrent = controller.position;
        _fake.clientRotationCurrent = movement.TargetRotation;

        var velocity = movement.CurrentVelocity;
        velocity.y = 0f;
        _fake.rbVelocity = controller.InverseTransformDirection(velocity);
        _fake.rbVelocityRaw = new Vector3(velocity.x, movement.IsGrounded ? 0f : movement.VerticalVelocity, velocity.z);
        // A player keeps moving, and sprinting, until its stop has eased out (PlayerController).
        if (pose.Gait != Gait.Still) _lastGait = pose.Gait;
        _fake.isMoving = movement.IsMoving;
        _fake.isSprinting = movement.IsMoving && _lastGait == Gait.Sprint;
        _fake.isGrounded = movement.IsGrounded;
        _fake.mapToolController.Active = pose.IsCheckingMap;

        // A remote player's camera is its PlayerLocalCamera's transform, here the fake's: its forward centres the
        // head's 40° look cone, its pitch tilts the head.
        _fake.localCamera.transform.rotation = controller.rotation;
        var victimCamera = pose.IsStaring ? Camera.main : null;
        var lookAt = victimCamera != null ? victimCamera.transform : null;
        if (pose.GlanceYaw is { } yaw)
        {
            _glancePoint.position = _visuals.headLookAtTransform.position +
                                     Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * GlancePointDistance;
            lookAt = _glancePoint;
        }

        _fakeEyes.lookAtActive = lookAt != null;
        _fakeEyes.lookAt = lookAt;
    }

    // Logged as the Glance ends, with its widest head turn: the head is still turning as it starts.
    // headLookAtTransform sits under bodyTopSideTransform, which PlayerAvatarVisuals turns after aiming the look: its
    // world yaw carries that turn on top of the look's 40° clamp while the body follows.
    private void TrackGlance()
    {
        var yaw = _hallucination.BodyPose.GlanceYaw;
        var bodyYaw = _body.eulerAngles.y;
        if (yaw != _glanceYaw)
        {
            if (_glanceYaw is { } ended)
            {
                LOG.Debug($"Body of {Name}: Glance at {Mathf.Repeat(ended, 360f):F0}° held {Time.time - _glanceStart:F1}s, body turned " +
                          $"{Mathf.DeltaAngle(_glanceBodyYawFrom, bodyYaw):F0}°, head looked up to " +
                          $"{_glanceWidestLook:F0}° off the camera, head mesh up to {_glanceWidestHead:F0}° off the body");
            }

            _glanceYaw = yaw;
            _glanceStart = Time.time;
            _glanceBodyYawFrom = bodyYaw;
            _glanceWidestLook = 0f;
            _glanceWidestHead = 0f;
        }

        if (_glanceYaw == null) return;
        // The game clamps the look around the camera, which leads the body's spring.
        _glanceWidestLook = Widest(_glanceWidestLook, _visuals.headLookAtTransform.eulerAngles.y,
            _fake.localCamera.transform.eulerAngles.y);
        _glanceWidestHead = Widest(_glanceWidestHead, _visuals.headSideTransform.eulerAngles.y, bodyYaw);
    }

    private static float Widest(float widest, float yaw, float fromYaw)
    {
        var off = Mathf.DeltaAngle(fromYaw, yaw);
        return Mathf.Abs(off) > Mathf.Abs(widest) ? off : widest;
    }

    private void LogPose()
    {
        var cameraForward = _fake.localCamera.transform.forward;
        var head = _visuals.headLookAtTransform;
        var headLook = Vector3.Angle(head.forward, cameraForward);
        var victim = _fakeEyes.lookAt != null
            ? $"{Vector3.Angle(_fakeEyes.lookAt.position - head.position, cameraForward):F0}°"
            : "none";
        LOG.Debug($"Body of {Name} [{_hallucination.DebugStateName}]: lean {Euler(_visuals.leanTransform)}, " +
                  $"sprint tilt {Euler(_visuals.tiltTransform)}, leg twist {Euler(_visuals.legTwistTransform)}, " +
                  $"head side-steer {Euler(_visuals.headSideTransform)}, " +
                  $"body {Quaternion.Angle(_body.rotation, _fake.clientRotationCurrent):F1}° behind its spring " +
                  $"target, head look {headLook:F0}° from the camera (Victim {victim}) | " +
                  $"Moving={_animator.GetBool(MovingKey)} Sprinting={_animator.GetBool(SprintingKey)} " +
                  $"Turning={_animator.GetBool(TurningKey)} Grabbing={_animator.GetBool(GrabbingKey)} " +
                  $"Falling={_animator.GetBool(FallingKey)} | footsteps blocked {_footstepsBlocked}");
    }

    private static string Euler(Transform transform)
    {
        var e = transform.localEulerAngles;
        return $"({Mathf.DeltaAngle(0f, e.x):F1}, {Mathf.DeltaAngle(0f, e.y):F1}, {Mathf.DeltaAngle(0f, e.z):F1})";
    }

    private static IEnumerable<FieldInfo> ReferenceFields()
    {
        return typeof(PlayerAvatarVisuals)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => typeof(Object).IsAssignableFrom(field.FieldType) ||
                            (field.FieldType.IsArray &&
                             typeof(Object).IsAssignableFrom(field.FieldType.GetElementType())));
    }

    // FromJsonOverwrite keeps the Imitated Player's references: each moves to the object at the same path under the
    // Cube, which stands for their 'Player Visuals'. Paths, not names: two fields point at a 'Mesh Flat'.
    private static List<string> RepointIntoClone(PlayerAvatarVisuals visuals, Transform sourceRoot, Transform cube)
    {
        var cleared = new List<string>();
        foreach (var field in ReferenceFields())
        {
            if (field.FieldType.IsArray)
            {
                if (field.GetValue(visuals) is not Array array) continue;
                var repointed = Array.CreateInstance(field.FieldType.GetElementType()!, array.Length);
                for (var i = 0; i < array.Length; i++)
                {
                    var value = (Object?)array.GetValue(i);
                    var clone = Repoint(value, sourceRoot, cube);
                    if (value != null && clone == null) cleared.Add($"{field.Name}[{i}]");
                    repointed.SetValue(clone, i);
                }

                field.SetValue(visuals, repointed);
                continue;
            }

            var reference = (Object?)field.GetValue(visuals);
            if (reference == null) continue;
            var repointedReference = Repoint(reference, sourceRoot, cube);
            if (repointedReference == null) cleared.Add(field.Name);
            field.SetValue(visuals, repointedReference);
        }

        return cleared;
    }

    /// <returns>Null when the reference has no counterpart in the clone.</returns>
    private static Object? Repoint(Object? reference, Transform sourceRoot, Transform cube)
    {
        var transform = TransformOf(reference);
        // An asset, shared by every player.
        if (transform == null || !transform.gameObject.scene.IsValid()) return reference;
        if (!transform.IsChildOf(sourceRoot)) return null;

        var path = PathUnder(sourceRoot, transform);
        var target = path.Length == 0 ? cube : cube.Find(path);
        if (target == null) return null;

        return reference switch
        {
            GameObject => target.gameObject,
            Transform => target,
            _ => target.GetComponent(reference!.GetType())
        };
    }

    private static int CountOutside(PlayerAvatarVisuals visuals, Transform root)
    {
        return ReferenceFields()
            .SelectMany(field => field.GetValue(visuals) switch
            {
                Array array => array.Cast<Object?>(),
                var value => new[] { (Object?)value }
            })
            .Select(TransformOf)
            .Count(transform => transform != null && transform.gameObject.scene.IsValid() &&
                                !transform.IsChildOf(root));
    }

    private static Transform? TransformOf(Object? reference)
    {
        return reference switch
        {
            Component component when component != null => component.transform,
            GameObject gameObject when gameObject != null => gameObject.transform,
            _ => null
        };
    }

    private static string PathUnder(Transform root, Transform transform)
    {
        var parts = new List<string>();
        for (var t = transform; t != root; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
