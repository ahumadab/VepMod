#if DEBUG
using System.Collections.Generic;
using System.Linq;
#if !VEPMOD_NO_VAD
using System;
using System.IO;
#endif
using UnityEngine;
using UnityEngine.AI;
using VepMod.Enemies.Whispral;
using VepMod.VepFramework;
#if !VEPMOD_NO_VAD
using VepMod.VepFramework.Audio;
#endif

namespace VepMod.Dev;

/// <summary>
///     Dev tools to test a Hallucination's gravity and fall animation without a Whispral or a second player.
///     Enabled by the "Developer / Enable Dev Tools" config flag; sits on the plugin's GameObject.
///     The HUD (<see cref="OnGUI" />) is the one list of keys the tester reads in game.
/// </summary>
public sealed class HallucinationDevTools : MonoBehaviour
{
    private const KeyCode CaptureKey = KeyCode.F6;
    private const KeyCode SpawnKey = KeyCode.F7;
    private const KeyCode TeleportKey = KeyCode.F8;
    private const KeyCode DropKey = KeyCode.F9;
    private const KeyCode HudKey = KeyCode.F10;
    private const KeyCode DespawnKey = KeyCode.F11;
    private const KeyCode SpawnWhispralKey = KeyCode.F1;
    private const KeyCode StunPreparingWhispralsKey = KeyCode.F2;
    private const KeyCode StunWhispralsKey = KeyCode.F3;
    private const KeyCode KillWhispralsKey = KeyCode.F4;
    private const KeyCode KillLocalPlayerKey = KeyCode.F5;
    private const KeyCode HurtLocalPlayerKey = KeyCode.F12;
    private const float StunDuration = 5f; // s
    private const int HealthStep = 10;

    private const float DropHeight = 6f; // m, drop test lift under an open sky
    private const float HallucinationHeight = 2f; // m, the CharacterController's height
    private const float CeilingClearance = 0.2f; // m
    private const float AimMaxDistance = 100f; // m, aim raycast range
    private const float NavSnapRadius = 5f; // m, NavMesh snap on spawn and teleport
    private const float NearestRefreshInterval = 0.4f; // s between two cache refreshes

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationDevTools>();

    private readonly List<Hallucination> _spawned = new();
    private Hallucination? _nearest;
    private float _nearestRefreshTimer;
    private bool _hudVisible = true;
    private int _otherPlayerIndex = -1;

    private void Update()
    {
        if (Input.GetKeyDown(CaptureKey))
        {
            if (ShiftHeld()) RequestCapture();
#if !VEPMOD_NO_VAD
            else ReplayWavFolderThroughVad();
#endif
        }
        if (Input.GetKeyDown(SpawnKey))
        {
            if (ShiftHeld()) CheckMapWithNearest();
            else if (AltHeld()) TurnRoundWithNearest();
            else if (CtrlHeld()) SpawnAtAim(NextOtherPlayer());
            else SpawnAtAim(PlayerAvatar.instance);
        }
        if (Input.GetKeyDown(TeleportKey))
        {
            if (ShiftHeld()) StalkWithNearest();
            else if (AltHeld()) CrossNearestTwo();
            else if (CtrlHeld()) ChainWithNearest();
            else TeleportNearestToAim();
        }
        if (Input.GetKeyDown(DropKey))
        {
            if (ShiftHeld()) MimicNearest();
            else if (AltHeld()) GlanceToBoundWithNearest();
            else if (CtrlHeld()) MimicNextToStalker();
            else DropNearest();
        }

        if (Input.GetKeyDown(HudKey))
        {
            if (ShiftHeld()) LogNearestRenderers();
            else _hudVisible = !_hudVisible;
        }
        if (Input.GetKeyDown(DespawnKey)) DespawnAll();
        if (Input.GetKeyDown(SpawnWhispralKey)) SpawnWhispralAtAim();
        if (Input.GetKeyDown(StunPreparingWhispralsKey)) StunWhisprals(true);
        if (Input.GetKeyDown(StunWhispralsKey)) StunWhisprals(false);
        if (Input.GetKeyDown(KillWhispralsKey))
        {
            if (ShiftHeld()) SendMimicCommands();
            else KillWhisprals();
        }
        if (Input.GetKeyDown(KillLocalPlayerKey))
        {
            if (ShiftHeld()) ReviveDeadPlayers();
            else KillLocalPlayer();
        }

        if (Input.GetKeyDown(HurtLocalPlayerKey))
        {
            if (ShiftHeld()) HealLocalPlayer();
            else HurtLocalPlayer();
        }

        _nearestRefreshTimer -= Time.deltaTime;
        if (_nearestRefreshTimer <= 0f)
        {
            _nearestRefreshTimer = NearestRefreshInterval;
            RefreshNearest();
        }
    }

    private static void RequestCapture()
    {
        if (!SemiFunc.RunIsLevel())
        {
            LOG.Warning("DevTools: Captures are saved in a level only.");
            return;
        }

        LOG.Info("DevTools: Capture requested, it starts when you talk.");
        VepMod.VoiceRecorder.RequestCapture();
    }

    // Each press imitates the next player after the last one picked, so every other player gets their turn.
    private PlayerAvatar? NextOtherPlayer()
    {
        var others = GameDirector.instance.PlayerList.Where(player => player && !player.isLocal).ToList();
        if (others.Count == 0) return null;
        _otherPlayerIndex = (_otherPlayerIndex + 1) % others.Count;
        return others[_otherPlayerIndex];
    }

    private void SpawnAtAim(PlayerAvatar? source)
    {
        if (source == null)
        {
            LOG.Warning("DevTools: no PlayerAvatar to imitate, cannot spawn.");
            return;
        }

        if (!TryGetAimPoint(out var point))
        {
            LOG.Warning("DevTools: no aim point for spawn.");
            return;
        }

        // A Hallucination off the NavMesh cannot start navigating.
        var spawnPos = NavMesh.SamplePosition(point, out var hit, NavSnapRadius, NavMesh.AllAreas)
            ? hit.position
            : point;

        var hallucination = Hallucination.Create(source, spawnPos);
        if (hallucination != null)
        {
            // Two dev Hallucinations of one player would share a name in every log line.
            _spawned.Add(hallucination);
            hallucination.name += $"#{_spawned.Count}";
            LOG.Info($"DevTools: spawned {hallucination.name} at {spawnPos} (dev Hallucinations: {_spawned.Count}).");
        }
        else
        {
            LOG.Warning("DevTools: Hallucination.Create failed (prefab not loaded?).");
        }
    }

    private void TeleportNearestToAim()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no Hallucination to teleport.");
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
        LOG.Info($"DevTools: teleported the Hallucination to {target}.");
    }

    private void StalkWithNearest()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no Hallucination to start Stalking.");
            return;
        }

        _nearest.DebugStartStalking();
        LOG.Info("DevTools: the nearest Hallucination starts Stalking.");
    }

    private void CheckMapWithNearest()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no Hallucination to check its map.");
            return;
        }

        _nearest.DebugCheckMap();
        LOG.Info("DevTools: the nearest Hallucination checks its map.");
    }

    private void TurnRoundWithNearest()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no Hallucination to turn round.");
            return;
        }

        _nearest.DebugTurnRoundNextWait();
        LOG.Info($"DevTools: {_nearest.name} turns round at the start of its next wait.");
    }

    private void ChainWithNearest()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no Hallucination to chain a Trip.");
            return;
        }

        _nearest.DebugChainNextArrival();
        LOG.Info($"DevTools: {_nearest.name} chains a Trip at its next arrival, within two Trips in a row.");
    }

    private void GlanceToBoundWithNearest()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no Hallucination to glance.");
            return;
        }

        _nearest.DebugGlanceToBoundNext();
        LOG.Info($"DevTools: {_nearest.name}'s next Glance goes to 120°, away from its body.");
    }

    private void DropNearest()
    {
        RefreshNearest();
        if (_nearest == null || _nearest.ControllerTransform == null)
        {
            LOG.Warning("DevTools: no Hallucination to drop.");
            return;
        }

        // Lifted into a ceiling, the controller lands on top of it, off the NavMesh: the lift stops below it.
        var controller = _nearest.ControllerTransform.position;
        var height = Physics.Raycast(controller + Vector3.up * HallucinationHeight, Vector3.up, out var ceiling,
            DropHeight, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? Mathf.Max(0f, ceiling.distance - CeilingClearance)
            : DropHeight;
        var lifted = controller + Vector3.up * height;
        _nearest.DebugTeleport(lifted);
        LOG.Info($"DevTools: drop test — lifted the Hallucination by {height:F1}m to {lifted}.");
    }

    private static bool ShiftHeld()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    private static bool CtrlHeld()
    {
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }

    private static bool AltHeld()
    {
        return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    }

    // Two Hallucinations crossing by chance is rare: each walks to the other's place, so they meet head-on.
    private static void CrossNearestTwo()
    {
        var cam = Camera.main;
        var origin = cam != null ? cam.transform.position : Vector3.zero;
        var nearest = FindObjectsOfType<Hallucination>()
            .Where(h => h != null && h.ControllerTransform != null)
            .OrderBy(h => (h.ControllerTransform.position - origin).sqrMagnitude)
            .Take(2)
            .ToList();
        if (nearest.Count < 2)
        {
            LOG.Warning("DevTools: two Hallucinations are needed to cross them.");
            return;
        }

        var first = nearest[0].ControllerTransform.position;
        var second = nearest[1].ControllerTransform.position;
        nearest[0].DebugWalkTo(second);
        nearest[1].DebugWalkTo(first);
        LOG.Info($"DevTools: {nearest[0].name} and {nearest[1].name} walk to each other's place, " +
                 $"{Vector3.Distance(first, second):F1} m apart.");
    }

    private void MimicNearest()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no Hallucination to perform a Mimic.");
            return;
        }

        _nearest.PlayMimic(false);
    }

    // Shift+F9 would pick the Hallucination Stalking the tester, the nearest one, rather than one beside it.
    private static void MimicNextToStalker()
    {
        var hallucinations = FindObjectsOfType<Hallucination>()
            .Where(hallucination => hallucination.ControllerTransform != null)
            .ToList();
        var stalker = hallucinations.FirstOrDefault(hallucination => hallucination.IsStalkingBeforeFlee);
        if (stalker == null)
        {
            LOG.Warning("DevTools: no Hallucination is Stalking.");
            return;
        }

        var stalkerPosition = stalker.ControllerTransform.position;
        var talker = hallucinations
            .Where(hallucination => hallucination != stalker)
            .OrderBy(hallucination => Vector3.Distance(hallucination.ControllerTransform.position, stalkerPosition))
            .FirstOrDefault();
        if (talker == null)
        {
            LOG.Warning("DevTools: no other Hallucination to perform a Mimic beside the one Stalking.");
            return;
        }

        LOG.Info($"DevTools: {talker.name} performs a Mimic " +
                 $"{Vector3.Distance(talker.ControllerTransform.position, stalkerPosition):F1} m from {stalker.name}, " +
                 $"which is in {stalker.DebugStateName}.");
        talker.PlayMimic(false);
    }

    // What Unity draws, where the Debug lines only say what the code asked for: pressed twice, the two dumps diff.
    private void LogNearestRenderers()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no Hallucination to inspect.");
            return;
        }

        var root = _nearest.transform;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        LOG.Info($"DevTools: {renderers.Length} renderers of {_nearest.name} " +
                 $"in {_nearest.DebugStateName}:");
        foreach (var renderer in renderers)
        {
            var material = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none";
            LOG.Info($"  {PathUnder(root, renderer.transform)}: active={renderer.gameObject.activeInHierarchy}, " +
                     $"enabled={renderer.enabled}, visible={renderer.isVisible}, scale={renderer.transform.lossyScale}, " +
                     $"layer={LayerMask.LayerToName(renderer.gameObject.layer)}, material={material}");
        }

        LOG.Info($"  Eyes: {_nearest.BodyPose.DebugEyes}");
    }

    // From the nearest rig bone (code_eye_left/…/eyelid_upper_mesh): the full path through the rig's separator objects
    // runs to 300 characters a line.
    private static string PathUnder(Transform root, Transform transform)
    {
        for (var parent = transform.parent; parent != null && parent != root; parent = parent.parent)
        {
            if (parent.name.StartsWith("code_"))
            {
                return parent == transform.parent ? $"{parent.name}/{transform.name}" : $"{parent.name}/…/{transform.name}";
            }
        }

        return transform.name;
    }

    private void DespawnAll()
    {
        var count = 0;
        foreach (var hallucination in _spawned)
        {
            if (hallucination != null)
            {
                Destroy(hallucination.gameObject);
                count++;
            }
        }

        _spawned.Clear();
        _nearest = null;
        LOG.Info($"DevTools: despawned {count} dev Hallucination(s).");
    }

#if !VEPMOD_NO_VAD
    /// <summary>
    ///     Replays every captured WAV (AudioFiles/) through the production VAD at the current sensitivity, and logs
    ///     accept or reject with the speech ratio per file, then the totals. A diagnostic without a microphone: it
    ///     checks the runtime decision, with the real native DLL the game loads, on real Voice Samples.
    /// </summary>
    private void ReplayWavFolderThroughVad()
    {
        var folder = VepMod.VoiceBanks.RootFolder;
        if (!Directory.Exists(folder))
        {
            LOG.Warning($"DevTools: no Voice Bank folder at {folder}.");
            return;
        }

        var files = Directory.GetFiles(folder, "*.wav", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            LOG.Warning("DevTools: no .wav files to replay through VAD.");
            return;
        }

        var sensitivity = VepMod.ConfigVadSensitivity.Value;
        var criteria = VadValidationCriteria.FromSensitivity(sensitivity);
        using var validator = new VadAudioValidator(criteria);

        var accepted = 0;
        var total = 0;
        var ratioSum = 0f;
        LOG.Info($"DevTools: replaying {files.Length} WAV through VAD (sensitivity={sensitivity}, ratio>={criteria.MinSpeechRatio:F2})");

        foreach (var file in files)
        {
            VoiceSample sample;
            try
            {
                sample = WavCodec.Decode(File.ReadAllBytes(file));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                LOG.Warning($"  skip (unreadable: {ex.Message}): {Path.GetFileName(file)}");
                continue;
            }

            var result = validator.Validate(sample.Samples, sample.SampleRate);
            total++;
            var ratio = result.Analysis?.SpeechRatio ?? 0f;
            ratioSum += ratio;
            if (result.IsValid) accepted++;

            LOG.Info($"  {(result.IsValid ? "ACCEPT" : "REJECT")} ratio={ratio:P0} [{sample.SampleRate}Hz] {Path.GetFileName(file)}");
        }

        var meanRatio = total > 0 ? ratioSum / total : 0f;
        LOG.Info($"DevTools: VAD replay done — accepted {accepted}/{total} (rejected {total - accepted}), mean speechRatio={meanRatio:P0}. " +
                 "Note: whole-clip analysis (no trailing-silence trim), matches the offline benchmark.");
    }
#endif

    private void RefreshNearest()
    {
        var cam = Camera.main;
        var origin = cam != null ? cam.transform.position : transform.position;

        _nearest = null;
        var bestSqr = float.MaxValue;
        foreach (var hallucination in FindObjectsOfType<Hallucination>())
        {
            if (hallucination == null || hallucination.ControllerTransform == null) continue;
            var sqr = (hallucination.ControllerTransform.position - origin).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                _nearest = hallucination;
            }
        }
    }

    private static void SpawnWhispralAtAim()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !TryGetAimPoint(out var point)) return;

        var setup = REPOLib.Modules.Enemies.RegisteredEnemies
            .FirstOrDefault(s => s.spawnObjects.Any(p => p.PrefabName.Contains("Whispral")));
        if (setup == null)
        {
            LOG.Warning("DevTools: Whispral EnemySetup not registered.");
            return;
        }

        REPOLib.Modules.Enemies.SpawnEnemy(setup, point, Quaternion.identity, false);
        LOG.Info($"DevTools: spawned a Whispral at {point}.");
    }

    private static void StunWhisprals(bool preparingOnly)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        foreach (var whispral in FindObjectsOfType<EnemyWhispral>())
        {
            if (preparingOnly && whispral.CurrentState != EnemyWhispral.State.PrepareAttach) continue;

            LOG.Info($"DevTools: stunning Whispral {whispral.GetComponent<Photon.Pun.PhotonView>().ViewID} in state {whispral.CurrentState}.");
            whispral.enemy.StateStunned.Set(StunDuration);
        }
    }

    private static void KillWhisprals()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        foreach (var whispral in FindObjectsOfType<EnemyWhispral>())
        {
            LOG.Info($"DevTools: killing Whispral {whispral.GetComponent<Photon.Pun.PhotonView>().ViewID} in state {whispral.CurrentState}.");
            whispral.enemy.Health.Hurt(int.MaxValue / 2, Vector3.up);
        }
    }

    private static void SendMimicCommands()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        foreach (var whispral in FindObjectsOfType<EnemyWhispral>())
        {
            var victim = whispral.playerTarget;
            if (whispral.CurrentState != EnemyWhispral.State.Attached || victim == null) continue;

            LOG.Info($"DevTools: Mimic command to {victim.playerName}.");
            whispral.SendMimicCommand(victim, false);
        }
    }

    private static void KillLocalPlayer()
    {
        var player = PlayerAvatar.instance;
        if (player == null) return;

        LOG.Info("DevTools: killing the local player.");
        player.playerHealth.Hurt(int.MaxValue / 2, false);
    }

    private static void HurtLocalPlayer()
    {
        var player = PlayerAvatar.instance;
        if (player == null) return;

        LOG.Info($"DevTools: hurting the local player by {HealthStep}.");
        player.playerHealth.Hurt(HealthStep, false);
    }

    private static void HealLocalPlayer()
    {
        var player = PlayerAvatar.instance;
        if (player == null) return;

        LOG.Info($"DevTools: healing the local player by {HealthStep}.");
        player.playerHealth.Heal(HealthStep);
    }

    private static void ReviveDeadPlayers()
    {
        // The game accepts a revive from the Master alone.
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        foreach (var player in GameDirector.instance.PlayerList)
        {
            if (!player || !player.isDisabled) continue;

            LOG.Info($"DevTools: reviving {player.playerName}.");
            player.Revive();
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

        var sb = $"VepMod Dev Tools — Hallucinations: {_spawned.Count}\n" +
                 $"F7 spawn yourself | F8 teleport | F9 drop | Shift+F9 Mimic | F10 hud | F11 despawn\n" +
                 "Shift+F8 the nearest Hallucination starts Stalking\n" +
                 "Alt+F8 the two nearest Hallucinations walk to each other's place\n" +
                 "Ctrl+F8 the nearest Hallucination chains a Trip at its next arrival (press twice: cap)\n" +
                 "Shift+F7 the nearest Hallucination checks its map\n" +
                 "Alt+F7 the nearest Hallucination turns round at the start of its next wait\n" +
                 "Alt+F9 the nearest Hallucination's next Glance goes to 120°\n" +
                 "Ctrl+F9 the Hallucination nearest to the one Stalking performs a Mimic\n" +
                 "Ctrl+F7 spawn a Hallucination of another player (the next one each press)\n" +
                 "Whispral (Master): F1 spawn | F2 stun preparing | F3 stun all | F4 kill\n" +
                 "Shift+F4 Mimic command to every attached Victim (Master)\n" +
                 "F5 kill yourself | Shift+F5 revive the dead (Master)\n" +
                 "F12 hurt yourself | Shift+F12 heal yourself\n" +
                 "Shift+F6 capture your voice now\n" +
                 "Shift+F10 log what the nearest Hallucination renders, eyes included\n";
#if !VEPMOD_NO_VAD
        sb += "F6 replay AudioFiles through VAD (see log)\n";
#endif

        if (_nearest != null && _nearest.Movement != null)
        {
            var m = _nearest.Movement;
            sb += $"\nNearest Hallucination:\n" +
                  $"  State      : {_nearest.DebugStateName}\n" +
                  $"  Gaze       : {(_nearest.Gaze.IsOn ? "on" : "off")}\n" +
                  $"  Grounded   : {m.IsGrounded}\n" +
                  $"  VertVel    : {m.VerticalVelocity:F2} m/s\n" +
                  $"  Gravity    : {Physics.gravity.y:F2} m/s²";
        }
        else
        {
            sb += "\nNo Hallucination in scene.";
        }

        sb += $"\n\nLast Capture: {VoiceSampleSharing.LastCaptureVerdict ?? "none yet"}";

        var style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 13,
            padding = new RectOffset(10, 10, 10, 10)
        };
        const float width = 440f;
        GUI.Box(new Rect(10, 10, width, style.CalcHeight(new GUIContent(sb), width)), sb, style);
    }
}
#endif
