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

    private const float DropHeight = 6f; // m, drop test lift
    private const float AimMaxDistance = 100f; // m, aim raycast range
    private const float NavSnapRadius = 5f; // m, NavMesh snap on spawn and teleport
    private const float NearestRefreshInterval = 0.4f; // s between two cache refreshes

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationDevTools>();

    private readonly List<Hallucination> _spawned = new();
    private Hallucination? _nearest;
    private float _nearestRefreshTimer;
    private bool _hudVisible = true;

    private void Update()
    {
        if (Input.GetKeyDown(CaptureKey))
        {
            if (ShiftHeld()) RequestCapture();
#if !VEPMOD_NO_VAD
            else ReplayWavFolderThroughVad();
#endif
        }
        if (Input.GetKeyDown(SpawnKey)) SpawnAtAim();
        if (Input.GetKeyDown(TeleportKey))
        {
            if (ShiftHeld()) StalkWithNearest();
            else TeleportNearestToAim();
        }
        if (Input.GetKeyDown(DropKey))
        {
            if (ShiftHeld()) MimicNearest();
            else DropNearest();
        }

        if (Input.GetKeyDown(HudKey)) _hudVisible = !_hudVisible;
        if (Input.GetKeyDown(DespawnKey)) DespawnAll();
        if (Input.GetKeyDown(SpawnWhispralKey)) SpawnWhispralAtAim();
        if (Input.GetKeyDown(StunPreparingWhispralsKey)) StunWhisprals(true);
        if (Input.GetKeyDown(StunWhispralsKey)) StunWhisprals(false);
        if (Input.GetKeyDown(KillWhispralsKey))
        {
            if (ShiftHeld()) SendMimicCommandsWithoutHallucination();
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

        // A Hallucination off the NavMesh cannot start navigating.
        var spawnPos = NavMesh.SamplePosition(point, out var hit, NavSnapRadius, NavMesh.AllAreas)
            ? hit.position
            : point;

        var hallucination = Hallucination.Create(source, spawnPos);
        if (hallucination != null)
        {
            _spawned.Add(hallucination);
            LOG.Info($"DevTools: spawned a Hallucination at {spawnPos} (dev Hallucinations: {_spawned.Count}).");
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

    private void DropNearest()
    {
        RefreshNearest();
        if (_nearest == null || _nearest.ControllerTransform == null)
        {
            LOG.Warning("DevTools: no Hallucination to drop.");
            return;
        }

        var lifted = _nearest.ControllerTransform.position + Vector3.up * DropHeight;
        _nearest.DebugTeleport(lifted);
        LOG.Info($"DevTools: drop test — lifted the Hallucination by {DropHeight}m to {lifted}.");
    }

    private static bool ShiftHeld()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
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

    // Names the Victim as the Imitated Player: a Victim is never imitated, so no Hallucination plays it.
    private static void SendMimicCommandsWithoutHallucination()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        foreach (var whispral in FindObjectsOfType<EnemyWhispral>())
        {
            var victim = whispral.playerTarget;
            if (whispral.CurrentState != EnemyWhispral.State.Attached || victim == null) continue;

            LOG.Info($"DevTools: Mimic command without a Hallucination to {victim.playerName}.");
            whispral.SendMimicCommand(victim, victim, false);
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
                 $"F7 spawn | F8 teleport | F9 drop | Shift+F9 Mimic | F10 hud | F11 despawn\n" +
                 "Shift+F8 the nearest Hallucination starts Stalking\n" +
                 "Whispral (Master): F1 spawn | F2 stun preparing | F3 stun all | F4 kill\n" +
                 "Shift+F4 Mimic command without a Hallucination (Master)\n" +
                 "F5 kill yourself | Shift+F5 revive the dead (Master)\n" +
                 "F12 hurt yourself | Shift+F12 heal yourself\n" +
                 "Shift+F6 capture your voice now\n";
#if !VEPMOD_NO_VAD
        sb += "F6 replay AudioFiles through VAD (see log)\n";
#endif

        if (_nearest != null && _nearest.Movement != null)
        {
            var m = _nearest.Movement;
            sb += $"\nNearest Hallucination:\n" +
                  $"  State      : {_nearest.DebugStateName}\n" +
                  $"  Gaze       : {(_nearest.Gaze.IsOn ? "on" : "off")}\n" +
                  $"  Grounded   : {m.DebugIsGrounded}\n" +
                  $"  Falling    : {m.IsFalling}\n" +
                  $"  VertVel    : {m.DebugVerticalVelocity:F2} m/s\n" +
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
        GUI.Box(new Rect(10, 10, 440, 340), sb, style);
    }
}
#endif
