#if DEBUG
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VepMod.Enemies.Whispral;
using VepMod.VepFramework;

namespace VepMod.Dev;

/// <summary>
///     Outils de dev pour tester rapidement la gravité / l'animation de chute du droid
///     sans dépendre du flux Whispral/multi. Activé via le flag config
///     "Developer / Enable Dev Tools". Attaché au GameObject du plugin.
///
///     Touches :
///       F7  — Spawn un droid au point visé (clone le joueur local, marche en solo)
///       F8  — Téléporte le droid le plus proche au point visé (le poser sur un rebord)
///       F9  — Drop test : soulève le droid le plus proche et le lâche sur place
///       F10 — Toggle de l'overlay HUD
///       F11 — Despawn les droids spawnés par l'outil
/// </summary>
public sealed class DroidDevTools : MonoBehaviour
{
    private const KeyCode SpawnKey = KeyCode.F7;
    private const KeyCode TeleportKey = KeyCode.F8;
    private const KeyCode DropKey = KeyCode.F9;
    private const KeyCode HudKey = KeyCode.F10;
    private const KeyCode DespawnKey = KeyCode.F11;

    private const float DropHeight = 6f; // m de soulèvement pour le drop test
    private const float AimMaxDistance = 100f; // portée du raycast de visée
    private const float NavSnapRadius = 5f; // rayon de snap NavMesh au spawn/teleport
    private const float NearestRefreshInterval = 0.4f; // s entre deux rafraîchissements du cache

    private static readonly VepLogger LOG = VepLogger.Create<DroidDevTools>();

    private readonly List<DroidController> _spawned = new();
    private DroidController? _nearest;
    private float _nearestRefreshTimer;
    private bool _hudVisible = true;

    private void Update()
    {
        if (Input.GetKeyDown(SpawnKey)) SpawnAtAim();
        if (Input.GetKeyDown(TeleportKey)) TeleportNearestToAim();
        if (Input.GetKeyDown(DropKey)) DropNearest();
        if (Input.GetKeyDown(HudKey)) _hudVisible = !_hudVisible;
        if (Input.GetKeyDown(DespawnKey)) DespawnAll();

        _nearestRefreshTimer -= Time.deltaTime;
        if (_nearestRefreshTimer <= 0f)
        {
            _nearestRefreshTimer = NearestRefreshInterval;
            RefreshNearest();
        }
    }

    private void SpawnAtAim()
    {
        var source = PlayerAvatar.instance;
        if (source == null)
        {
            LOG.Warning("DevTools: no local PlayerAvatar to clone, cannot spawn.");
            return;
        }

        if (!TryGetAimPoint(out var point))
        {
            LOG.Warning("DevTools: no aim point for spawn.");
            return;
        }

        // Snap sur le NavMesh pour que le droid démarre sa navigation correctement.
        var spawnPos = NavMesh.SamplePosition(point, out var hit, NavSnapRadius, NavMesh.AllAreas)
            ? hit.position
            : point;

        var droid = DroidController.Create(source, spawnPos);
        if (droid != null)
        {
            _spawned.Add(droid);
            LOG.Info($"DevTools: spawned droid at {spawnPos} (total dev droids: {_spawned.Count}).");
        }
        else
        {
            LOG.Warning("DevTools: DroidController.Create failed (prefab not loaded?).");
        }
    }

    private void TeleportNearestToAim()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no droid to teleport.");
            return;
        }

        if (!TryGetAimPoint(out var point))
        {
            LOG.Warning("DevTools: no aim point for teleport.");
            return;
        }

        var target = NavMesh.SamplePosition(point, out var hit, NavSnapRadius, NavMesh.AllAreas)
            ? hit.position
            : point;

        _nearest.DebugTeleport(target);
        LOG.Info($"DevTools: teleported droid to {target}.");
    }

    private void DropNearest()
    {
        RefreshNearest();
        if (_nearest == null || _nearest.ControllerTransform == null)
        {
            LOG.Warning("DevTools: no droid to drop.");
            return;
        }

        var lifted = _nearest.ControllerTransform.position + Vector3.up * DropHeight;
        _nearest.DebugTeleport(lifted);
        LOG.Info($"DevTools: drop test — lifted droid by {DropHeight}m to {lifted}.");
    }

    private void DespawnAll()
    {
        var count = 0;
        foreach (var droid in _spawned)
        {
            if (droid != null)
            {
                Destroy(droid.gameObject);
                count++;
            }
        }

        _spawned.Clear();
        _nearest = null;
        LOG.Info($"DevTools: despawned {count} dev droid(s).");
    }

    private void RefreshNearest()
    {
        var cam = Camera.main;
        var origin = cam != null ? cam.transform.position : transform.position;

        _nearest = null;
        var bestSqr = float.MaxValue;
        foreach (var droid in FindObjectsOfType<DroidController>())
        {
            if (droid == null || droid.ControllerTransform == null) continue;
            var sqr = (droid.ControllerTransform.position - origin).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                _nearest = droid;
            }
        }
    }

    private static bool TryGetAimPoint(out Vector3 point)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            point = Vector3.zero;
            return false;
        }

        var ray = new Ray(cam.transform.position, cam.transform.forward);
        point = Physics.Raycast(ray, out var hit, AimMaxDistance)
            ? hit.point
            : cam.transform.position + cam.transform.forward * 10f;
        return true;
    }

    private void OnGUI()
    {
        if (!_hudVisible) return;

        var sb = $"VepMod Dev Tools — droids: {_spawned.Count}\n" +
                 $"F7 spawn | F8 teleport | F9 drop | F10 hud | F11 despawn\n";

        if (_nearest != null && _nearest.Movement != null)
        {
            var m = _nearest.Movement;
            sb += $"\nNearest droid:\n" +
                  $"  State      : {_nearest.DebugStateName}\n" +
                  $"  Grounded   : {m.DebugIsGrounded}\n" +
                  $"  Falling    : {_nearest.IsFalling}\n" +
                  $"  VertVel    : {m.DebugVerticalVelocity:F2} m/s\n" +
                  $"  Gravity    : {Physics.gravity.y:F2} m/s²";
        }
        else
        {
            sb += "\nNo droid in scene.";
        }

        var style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 13,
            padding = new RectOffset(10, 10, 10, 10)
        };
        GUI.Box(new Rect(10, 10, 320, 170), sb, style);
    }
}
#endif
