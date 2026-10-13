#if DEBUG
using System.Collections.Generic;
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
using VepMod.Patchs;
#endif

namespace VepMod.Dev;

/// <summary>
///     Dev tools to test a Hallucination's gravity and fall animation without a Whispral or a second player.
///     Enabled by the "Developer / Enable Dev Tools" config flag; sits on the plugin's GameObject.
///
///     Keys:
///       F7  — spawn a Hallucination of the local player at the aimed point (works in singleplayer)
///       F8  — teleport the nearest Hallucination to the aimed point (to put it on a ledge)
///       F9  — drop test: lift the nearest Hallucination and let it fall in place
///       F10 — toggle the HUD overlay
///       F11 — despawn the Hallucinations the tool spawned
/// </summary>
public sealed class DroidDevTools : MonoBehaviour
{
#if !VEPMOD_NO_VAD
    private const KeyCode VadReplayKey = KeyCode.F6;
#endif
    private const KeyCode SpawnKey = KeyCode.F7;
    private const KeyCode TeleportKey = KeyCode.F8;
    private const KeyCode DropKey = KeyCode.F9;
    private const KeyCode HudKey = KeyCode.F10;
    private const KeyCode DespawnKey = KeyCode.F11;

    private const float DropHeight = 6f; // m, drop test lift
    private const float AimMaxDistance = 100f; // m, aim raycast range
    private const float NavSnapRadius = 5f; // m, NavMesh snap on spawn and teleport
    private const float NearestRefreshInterval = 0.4f; // s between two cache refreshes

    private static readonly VepLogger LOG = VepLogger.Create<DroidDevTools>();

    private readonly List<DroidController> _spawned = new();
    private DroidController? _nearest;
    private float _nearestRefreshTimer;
    private bool _hudVisible = true;

    private void Update()
    {
#if !VEPMOD_NO_VAD
        if (Input.GetKeyDown(VadReplayKey)) ReplayWavFolderThroughVad();
#endif
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

        // A Hallucination off the NavMesh cannot start navigating.
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

#if !VEPMOD_NO_VAD
    /// <summary>
    ///     Replays every captured WAV (AudioFiles/) through the production VAD at the current sensitivity, and logs
    ///     accept or reject with the speech ratio per file, then the totals. A diagnostic without a microphone: it
    ///     checks the runtime decision, with the real native DLL the game loads, on real Voice Samples.
    /// </summary>
    private void ReplayWavFolderThroughVad()
    {
        var folder = Path.Combine(Application.dataPath, "AudioFiles");
        if (!Directory.Exists(folder))
        {
            LOG.Warning($"DevTools: no AudioFiles folder at {folder}.");
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
            if (!TryLoadWav(file, out var samples, out var sr))
            {
                LOG.Warning($"  skip (unreadable): {Path.GetFileName(file)}");
                continue;
            }

            var result = validator.Validate(samples, sr);
            total++;
            var ratio = result.Analysis?.SpeechRatio ?? 0f;
            ratioSum += ratio;
            if (result.IsValid) accepted++;

            LOG.Info($"  {(result.IsValid ? "ACCEPT" : "REJECT")} ratio={ratio:P0} [{sr}Hz] {Path.GetFileName(file)}");
        }

        var meanRatio = total > 0 ? ratioSum / total : 0f;
        LOG.Info($"DevTools: VAD replay done — accepted {accepted}/{total} (rejected {total - accepted}), mean speechRatio={meanRatio:P0}. " +
                 "Note: whole-clip analysis (no trailing-silence trim), matches the offline benchmark.");
    }

    /// <summary>
    ///     Reads a mono PCM16 WAV written by WavFileManager, with its canonical 44-byte header.
    /// </summary>
    private static bool TryLoadWav(string path, out float[] samples, out int sampleRate)
    {
        samples = Array.Empty<float>();
        sampleRate = 0;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length <= 44) return false;
            sampleRate = BitConverter.ToInt32(bytes, 24); // standard WAV sample rate offset
            if (sampleRate <= 0) return false;

            var pcmLength = bytes.Length - 44;
            var pcm = new byte[pcmLength];
            Array.Copy(bytes, 44, pcm, 0, pcmLength);
            samples = AudioFilters.ConvertBytesToFloats(pcm);
            return true;
        }
        catch
        {
            return false;
        }
    }
#endif

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
#if !VEPMOD_NO_VAD
        sb += "F6 replay AudioFiles through VAD (see log)\n";
#endif

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

#if !VEPMOD_NO_VAD
        var mimics = VepFinder.LocalMimics;
        var sensitivity = VepMod.ConfigVadSensitivity.Value;
        if (mimics != null && mimics.LastVadResult.HasValue)
        {
            var r = mimics.LastVadResult.Value;
            var verdict = r.IsValid ? "ACCEPT" : $"REJECT ({r.RejectionReason})";
            sb += $"\n\nVAD [{sensitivity}]: {verdict}\n  {r.Analysis}";
        }
        else
        {
            sb += $"\n\nVAD [{sensitivity}]: (no clip recorded yet)";
        }
#endif

        var style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 13,
            padding = new RectOffset(10, 10, 10, 10)
        };
        GUI.Box(new Rect(10, 10, 380, 240), sb, style);
    }
}
#endif
