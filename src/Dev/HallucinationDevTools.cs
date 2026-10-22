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
    private const float StunDuration = 5f; // s
    private const int HealthStep = 10;

    private const float DropHeight = 6f; // m, drop test lift under an open sky
    private const float HallucinationHeight = 2f; // m, the CharacterController's height
    private const float CeilingClearance = 0.2f; // m
    private const float AimMaxDistance = 100f; // m, aim raycast range
    private const float NavSnapRadius = 5f; // m, NavMesh snap on spawn and teleport
    private const float NearestRefreshInterval = 0.4f; // s between two cache refreshes
    private const float SpawnTimerRunOut = 0.01f; // s
    private const float EndOfLevelReplayDuration = 1f; // s

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationDevTools>();

    private readonly List<Hallucination> _spawned = new();
    private float _endOfLevelReplayTimer;
    private Hallucination? _nearest;
    private float _nearestRefreshTimer;
    private bool _hudVisible = true;
    private int _otherPlayerIndex = -1;

    private void Update()
    {
        if (Pressed(KeyCode.Keypad0)) SpawnAtAim(PlayerAvatar.instance);
        if (Pressed(KeyCode.Keypad0, true)) SpawnAtAim(NextOtherPlayer());
        if (Pressed(KeyCode.Keypad1)) TeleportNearestToAim();
        if (Pressed(KeyCode.Keypad1, true)) DropNearest();
        if (Pressed(KeyCode.Keypad2)) StalkWithNearest();
        if (Pressed(KeyCode.Keypad2, true)) MimicNextToStalker();
        if (Pressed(KeyCode.Keypad3)) MimicNearest();
        if (Pressed(KeyCode.Keypad3, true)) LogNearestRenderers();
        if (Pressed(KeyCode.Keypad4)) CheckMapWithNearest();
        if (Pressed(KeyCode.Keypad4, true)) TurnRoundWithNearest();
        if (Pressed(KeyCode.Keypad5)) GlanceToBoundWithNearest();
        if (Pressed(KeyCode.Keypad5, true)) ForceEveryNextStalkingRoll();
        if (Pressed(KeyCode.Keypad6)) CrossNearestTwo();
        if (Pressed(KeyCode.Keypad7)) ChainWithNearest();
        if (Pressed(KeyCode.Keypad7, true)) RunOutSpawnTimers();
        if (Pressed(KeyCode.Keypad8)) ChangeMindWithNearest();
        if (Pressed(KeyCode.Keypad8, true)) StartEndOfLevelReplay();
        ReplayEndOfLevelReturns();
        if (Pressed(KeyCode.Keypad9)) DespawnAll();
        if (Pressed(KeyCode.Keypad9, true)) WithholdNextLungeVoiceSample();

        if (Pressed(KeyCode.KeypadDivide)) SpawnWhispralAtAim();
        if (Pressed(KeyCode.KeypadDivide, true)) SendMimicCommands();
        if (Pressed(KeyCode.KeypadMultiply)) StunWhisprals(true);
        if (Pressed(KeyCode.KeypadMultiply, true)) StunWhisprals(false);
        if (Pressed(KeyCode.KeypadMinus)) KillWhisprals();
        if (Pressed(KeyCode.KeypadMinus, true)) SwitchLethalHallucinations();

        if (Pressed(KeyCode.KeypadPlus)) HurtLocalPlayer();
        if (Pressed(KeyCode.KeypadPlus, true)) HealLocalPlayer();
        if (Pressed(KeyCode.KeypadPeriod)) KillLocalPlayer();
        if (Pressed(KeyCode.KeypadPeriod, true)) ReviveDeadPlayers();

        if (Pressed(KeyCode.KeypadEnter)) _hudVisible = !_hudVisible;
        if (Pressed(KeyCode.KeypadEnter, true)) RequestCapture();
#if !VEPMOD_NO_VAD
        if (Pressed(KeyCode.Keypad6, true)) ReplayWavFolderThroughVad();
#endif

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

        var bypassed = _nearest.DebugStartStalking();
        LOG.Info(bypassed
            ? $"DevTools: {_nearest.name} starts Stalking, bypassing the rule of one Stalking at a time: " +
              $"{bypassed!.name} is Stalking."
            : $"DevTools: {_nearest.name} starts Stalking.");
    }

    private void CheckMapWithNearest()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no Hallucination to check its map.");
            return;
        }

        if (_nearest.BodyPose.MapLookPoint == null)
        {
            LOG.Warning($"DevTools: {_nearest.name} has no map (the local player's Hallucination has none): " +
                        "Ctrl+Num0 spawns another player's.");
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

    private void ChangeMindWithNearest()
    {
        RefreshNearest();
        if (_nearest == null)
        {
            LOG.Warning("DevTools: no Hallucination to change its mind.");
            return;
        }

        _nearest.DebugChangeMindNow();
        LOG.Info($"DevTools: {_nearest.name} ({_nearest.DebugStateName}) changes its mind now if on a Trip that has " +
                 "not yet, else on its next Trip.");
    }

    private static void ForceEveryNextStalkingRoll()
    {
        var hallucinations = FindObjectsOfType<Hallucination>();
        foreach (var hallucination in hallucinations)
        {
            hallucination.DebugForceNextStalkingRoll();
        }

        LOG.Info($"DevTools: the next Stalking roll of {hallucinations.Length} Hallucinations succeeds.");
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

    // The keypad keeps the dev keys clear of the F keys other debug mods (Imperium) take. Ctrl is the only modifier:
    // on Windows, Shift turns a keypad digit into a navigation key while Num Lock is on, and Alt with keypad digits
    // types a character code.
    private static bool Pressed(KeyCode key, bool ctrl = false)
    {
        return Input.GetKeyDown(key) &&
               (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) == ctrl;
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

    // Num3 would pick the Hallucination Stalking the tester, the nearest one, rather than one beside it.
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

    // Both instances on one PC share their Voice Banks on disk: neither can empty its own.
    private static void WithholdNextLungeVoiceSample()
    {
        Hallucination.DebugNextLungeFindsNoVoiceSample();
        LOG.Info("DevTools: the next Lunge finds no Voice Sample.");
    }

    private void DespawnAll()
    {
        var count = 0;
        foreach (var hallucination in _spawned)
        {
            if (hallucination != null)
            {
                hallucination.Remove();
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

    // Dead Whisprals included: EnemyParent stays active while its Whispral is despawned.
    private static IEnumerable<(EnemyParent Parent, EnemyWhispral Whispral, int ViewID)> AllWhisprals()
    {
        if (!EnemyDirector.instance) yield break;

        foreach (var parent in EnemyDirector.instance.enemiesSpawned)
        {
            if (!parent) continue;
            var whispral = parent.GetComponentInChildren<EnemyWhispral>(true);
            if (whispral) yield return (parent, whispral, whispral.GetComponent<Photon.Pun.PhotonView>().ViewID);
        }
    }

    private static void RunOutSpawnTimers()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        foreach (var (parent, _, viewID) in AllWhisprals())
        {
            if (!parent.Spawned) continue;

            LOG.Info($"DevTools: running out the spawn timer of Whispral {viewID} " +
                     $"(was {parent.SpawnedTimer:F1} s).");
            parent.SpawnedTimerPause(0f);
            // Above zero: a timer at exactly zero is the game's despawn order, not one that ran out.
            parent.SpawnedTimer = SpawnTimerRunOut;
        }
    }

    // EnemyDirector.Update's rule once every extraction point is done: a present enemy with no player close despawns
    // once, and every frame after, a dead one with more than 30 s left comes back at once.
    private void StartEndOfLevelReplay()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        _endOfLevelReplayTimer = EndOfLevelReplayDuration;
        foreach (var (parent, _, viewID) in AllWhisprals())
        {
            if (!parent.Spawned) continue;

            if (parent.playerClose)
            {
                LOG.Info($"DevTools: end of level, Whispral {viewID} stays: a player is close.");
                continue;
            }

            LOG.Info($"DevTools: end of level, SpawnedTimerSet(0f) on Whispral {viewID}.");
            parent.SpawnedTimerPause(0f);
            parent.SpawnedTimerSet(0f);
        }
    }

    private void ReplayEndOfLevelReturns()
    {
        if (_endOfLevelReplayTimer <= 0f) return;

        _endOfLevelReplayTimer -= Time.deltaTime;
        foreach (var (parent, _, viewID) in AllWhisprals())
        {
            if (parent.Spawned || parent.DespawnedTimer <= 30f) continue;

            LOG.Info($"DevTools: end of level, DespawnedTimerSet(0f) on Whispral {viewID} " +
                     $"({parent.DespawnedTimer:F0} s left).");
            parent.DespawnedTimerSet(0f);
        }
    }

    private static string WhispralsSummary()
    {
        var summary = "";
        foreach (var (parent, whispral, viewID) in AllWhisprals())
        {
            summary += $"\n  {viewID}: ";
            summary += parent.Spawned
                ? $"{whispral.CurrentState}, spawn timer {parent.SpawnedTimer:F0} s, " +
                  $"nearest player {EnemyWhispral.NearestPlayerDistance(whispral.transform.position):F0} m"
                : $"despawned, back in {parent.DespawnedTimer:F0} s";
        }

        return summary.Length > 0 ? summary : " none";
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

    private static void SwitchLethalHallucinations()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        var option = VepMod.ConfigLethalHallucinations;
        option.Value = !option.Value;
        LOG.Info($"DevTools: Lethal Hallucinations switched {(option.Value ? "on" : "off")}.");
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
                 "Keypad, Num Lock on. Num0 spawn yourself | Ctrl+Num0 another player (the next one each press)\n" +
                 "  outside any Haunting: a death leaves them, Num9 removes them\n" +
                 "Num1 teleport to aim | Ctrl+Num1 drop | Num9 despawn all | NumEnter hud\n" +
                 "Ctrl+Num9 the next Lunge finds no Voice Sample\n" +
                 "Nearest Hallucination:\n" +
                 "  Num2 starts Stalking | Ctrl+Num2 the one nearest to it performs a Mimic\n" +
                 "  Num3 performs a Mimic | Ctrl+Num3 log what it renders, eyes included\n" +
                 "  Num4 checks its map | Ctrl+Num4 turns round at the start of its next wait\n" +
                 "  Num5 its next Glance goes to 120° | Ctrl+Num5 every Hallucination's next Stalking roll succeeds\n" +
                 "  Num6 the two nearest walk to each other's place\n" +
                 "  Num7 chains a Trip at its next arrival (press twice: cap)\n" +
                 "  Num8 changes its mind on its Trip (press twice: cap)\n" +
                 "Whispral (Master): Num/ spawn | Num* stun preparing | Ctrl+Num* stun all | Num- kill\n" +
                 "Ctrl+Num/ Mimic command to every attached Victim (Master)\n" +
                 "Ctrl+Num7 run out every Whispral's spawn timer (Master)\n" +
                 "Ctrl+Num8 replay the end-of-level rule on every Whispral (Master)\n" +
                 "Ctrl+Num- switch Lethal Hallucinations (Master)\n" +
                 "Num. kill yourself | Ctrl+Num. revive the dead (Master)\n" +
                 "Num+ hurt yourself | Ctrl+Num+ heal yourself\n" +
                 "Ctrl+NumEnter capture your voice now\n";
#if !VEPMOD_NO_VAD
        sb += "Ctrl+Num6 replay AudioFiles through VAD (see log)\n";
#endif

        if (_nearest != null && _nearest.Movement != null)
        {
            var m = _nearest.Movement;
            sb += $"\nNearest Hallucination:\n" +
                  $"  State      : {_nearest.DebugStateName}\n" +
                  $"  Gaze       : {(_nearest.Gaze.IsOn ? "on" : "off")}\n" +
                  $"  Lunge count: {_nearest.LungeCountdown}\n" +
                  $"  Grounded   : {m.IsGrounded}\n" +
                  $"  VertVel    : {m.VerticalVelocity:F2} m/s\n" +
                  $"  Gravity    : {Physics.gravity.y:F2} m/s²";
        }
        else
        {
            sb += "\nNo Hallucination in scene.";
        }

        sb += $"\n\nWhisprals (timers on the Master):{WhispralsSummary()}";
        sb += $"\n\nLethal Hallucinations: {LethalHallucinations.Summary}" +
              $"\nLast Capture: {VoiceSampleSharing.LastCaptureVerdict ?? "none yet"}";

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
