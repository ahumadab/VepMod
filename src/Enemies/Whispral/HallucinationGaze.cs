using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The Victim's Gaze on one Hallucination, and the line of sight it needs, which the nameplate shares.
/// </summary>
internal sealed class HallucinationGaze
{
    private const float MaxAngle = 50f; // °
    private const float MaxDistance = 30f; // m

    private const string OutOfView = "out of view";
    private const string Hidden = "hidden";
    private const string Head = "head";
    private const string Chest = "chest";

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationGaze>();

    private static readonly float MinDot = Mathf.Cos(MaxAngle * Mathf.Deg2Rad);

    // The game's vision rule without the Player layer: every teammate is hidden from the Victim, so none blocks the view.
    private static int? _obstructionMask;

    private readonly Transform? _chest;
    private readonly Transform _controller;
    private readonly Transform? _head;
    private readonly string _name;
    private readonly Transform _root;
    private string _sight = OutOfView;

    public HallucinationGaze(Transform root, Transform controller, Transform? head, Transform? chest, string name)
    {
        _root = root;
        _controller = controller;
        _head = head;
        _chest = chest;
        _name = name;
    }

    public bool IsOn { get; private set; }

    /// <summary>
    ///     The first of the head and the chest that nothing hides from the Victim's camera, wherever they look; null
    ///     when both are hidden.
    /// </summary>
    public string? VisiblePart { get; private set; }

    private static int ObstructionMask =>
        _obstructionMask ??= SemiFunc.LayerMaskGetVisionObstruct() & ~LayerMask.GetMask("Player");

    /// <summary>
    ///     Called from LateUpdate, before the face and the nameplate read it; the stare state reads it the next frame.
    /// </summary>
    public void Update()
    {
        var camera = Camera.main;
        VisiblePart = camera != null ? FindVisiblePart(camera.transform.position) : null;
        var sight = camera == null || !IsInView(camera.transform) ? OutOfView : VisiblePart ?? Hidden;

        if (sight != _sight)
        {
            LOG.Debug($"Gaze on the Hallucination of {_name}: {sight}");
            _sight = sight;
        }

        IsOn = sight is Head or Chest;
    }

    private string? FindVisiblePart(Vector3 eye)
    {
        if (_head != null && CanSee(eye, _head.position)) return Head;
        if (_chest != null && CanSee(eye, _chest.position)) return Chest;
        return null;
    }

    private bool IsInView(Transform camera)
    {
        var toHallucination = _controller.position + Vector3.up - camera.position;
        var distance = toHallucination.magnitude;
        return distance <= MaxDistance && Vector3.Dot(camera.forward, toHallucination / distance) >= MinDot;
    }

    // The part sits inside the Hallucination's own CharacterController: hitting it first means nothing stands between.
    private bool CanSee(Vector3 eye, Vector3 part)
    {
        return !Physics.Linecast(eye, part, out var hit, ObstructionMask, QueryTriggerInteraction.Ignore) ||
               hit.transform.IsChildOf(_root);
    }
}
