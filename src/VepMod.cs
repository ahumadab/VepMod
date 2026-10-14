using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
#if DEBUG
using VepMod.Dev;
#endif
using VepMod.Enemies.Whispral;
using VepMod.VepFramework;
#if !VEPMOD_NO_VAD
using VepMod.VepFramework.Audio;
#endif
using VepMod.VepFramework.Config;
using VepMod.VepFramework.Structures.Range;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod;

public static class ConfigRanges
{
    public static readonly RangeValue<float> Volume = RangeValue.Percentage();
    public static readonly RangeValue<int> SamplesPerPlayer = RangeValue.Int(5, 20, 20);

    // Linked min/max pairs: corrected automatically when min > max
    public static readonly MinMaxRange<float> ShareDelay = MinMaxRange.Float(
        10f, 60f, 10f,
        10f, 120f, 30f);

    public static readonly MinMaxRange<float> VoiceDelay = MinMaxRange.Float(
        6f, 30f, 8f,
        6f, 30f, 15f);

    // Guard applied before VAD
    public static readonly RangeValue<float> AudioMinDuration = RangeValue.Float(0.1f, 2f, 0.3f);
}

[BepInPlugin("com.vep.vepMod", "VepMod", "1.1.0")]
[BepInDependency("REPOLib")]
public class VepMod : BaseUnityPlugin
{
    public static ConfigEntry<float> ConfigVoiceVolume;

    // Voice Sample sharing between clients
    public static BoundMinMaxRange<float> ShareDelay;
    public static ConfigEntry<int> ConfigSamplesPerPlayer;

    // Mimic commands (while Haunted)
    public static BoundMinMaxRange<float> VoiceDelay;
    public static ConfigEntry<bool> ConfigVoiceFilterEnabled;

#if DEBUG
    public static ConfigEntry<bool> ConfigEnableDevTools;
#endif

    // Audio validation (VAD)
    public static ConfigEntry<float> ConfigAudioMinDuration;
#if !VEPMOD_NO_VAD
    public static ConfigEntry<bool> ConfigVadEnabled;
    public static ConfigEntry<VadSensitivity> ConfigVadSensitivity;
#endif

    private static readonly VepLogger LOG = VepLogger.Create<VepMod>();

    public static VoiceBanks VoiceBanks { get; private set; }

    public static VoiceRecorder VoiceRecorder { get; private set; }

    /// <summary>Null when the VAD is disabled or failed to load.</summary>
    public static SpeechCheck? SpeechCheck { get; private set; }

#if !VEPMOD_NO_VAD
    private static VadAudioValidator? _vad;
#endif

    private void Awake()
    {
        PreventDelete();

        InitConfig();

        VoiceBanks = new VoiceBanks(Path.Combine(Application.dataPath, "AudioFiles"),
            () => ConfigSamplesPerPlayer.Value);
        LOG.Debug($"Voice Bank store created at {VoiceBanks.RootFolder}.");
        VoiceRecorder = new VoiceRecorder(VepLogger.Create<VoiceRecorder>(true).Debug);
#if !VEPMOD_NO_VAD
        SpeechCheck = CreateSpeechCheck();
#endif

        new Harmony("VepMod.Plugin").PatchAll();
        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");

#if DEBUG
        if (ConfigEnableDevTools.Value)
        {
            gameObject.AddComponent<DroidDevTools>();
            Logger.LogInfo("VepMod dev tools enabled (F7 spawn / F8 teleport / F9 drop / F10 HUD / F11 despawn).");
        }
#endif

        StartCoroutine(DroidPrefabLoader.PreloadAsync(success =>
        {
            if (success)
            {
                Logger.LogInfo("Hallucination prefab preloaded successfully.");
            }
            else
            {
                Logger.LogWarning("Hallucination prefab preload failed - hallucinations may not work.");
            }
        }));
    }

    private void OnDestroy()
    {
#if !VEPMOD_NO_VAD
        _vad?.Dispose();
#endif
    }

#if !VEPMOD_NO_VAD
    private static SpeechCheck? CreateSpeechCheck()
    {
        if (!ConfigVadEnabled.Value)
        {
            LOG.Info("VAD disabled in the config: Captures will not be checked for speech.");
            return null;
        }

        try
        {
            var criteria = VadValidationCriteria.FromSensitivity(ConfigVadSensitivity.Value);
            var vad = new VadAudioValidator(criteria);
            _vad = vad;
            LOG.Info($"VAD handle created (sensitivity={ConfigVadSensitivity.Value}, " +
                     $"speechRatio>={criteria.MinSpeechRatio:F2}).");
            return (speech, sampleRate) =>
            {
                var result = vad.Validate(speech, sampleRate);
                return (result.IsValid, result.ToString());
            };
        }
        catch (Exception ex)
        {
            LOG.Warning($"VAD initialization failed, Captures will not be checked for speech: {ex.Message}");
            return null;
        }
    }
#endif

    private void InitConfig()
    {
        Logger.LogInfo("Initializing config for VepMod...");

        // General settings
        ConfigVoiceVolume = Config.BindRange("General", "Volume", ConfigRanges.Volume, "Volume of the mimic voices.");

        // Voice Sample sharing between clients
        ShareDelay = Config.BindMinMax("Audio Sharing",
            "Share Min Delay", "Share Max Delay", ConfigRanges.ShareDelay,
            "Minimum time between sharing audio clips with other players.",
            "Maximum time between sharing audio clips with other players.");
        ConfigSamplesPerPlayer = Config.BindRange("Audio Sharing", "Samples Per Player", ConfigRanges.SamplesPerPlayer,
            "Maximum number of audio samples stored per player.");

        // Mimic commands (while Haunted)
        VoiceDelay = Config.BindMinMax("Voice Playback",
            "Voice Min Delay", "Voice Max Delay", ConfigRanges.VoiceDelay,
            "Minimum time between voice playback commands during Whispral debuff.",
            "Maximum time between voice playback commands during Whispral debuff.");
        ConfigVoiceFilterEnabled = Config.Bind("Voice Playback", "Voice Filter Enabled?", false,
            new ConfigDescription(
                "Turning this will enable modifiable filters for which enemies can play back voices during the Whispral debuff."));

#if DEBUG
        // Developer tools (dev build only, off by default)
        ConfigEnableDevTools = Config.Bind("Developer", "Enable Dev Tools", false,
            new ConfigDescription(
                "Enables in-game developer tools for testing the droid: spawn/teleport/drop-test keybinds (F7-F11) and a debug HUD. For development only."));
#endif

        // Audio validation (VAD)
        ConfigAudioMinDuration = Config.BindRange("Audio Quality", "Min Duration", ConfigRanges.AudioMinDuration,
            "Minimum duration in seconds. Shorter recordings are rejected.");
#if !VEPMOD_NO_VAD
        ConfigVadEnabled = Config.Bind("Audio Quality", "VAD Enabled", true,
            "Enable Voice Activity Detection (VAD) for speech filtering. Uses WebRTC ML model (requires x64). Disable if you have issues.");
        ConfigVadSensitivity = Config.Bind("Audio Quality", "VAD Sensitivity", VadSensitivity.Balanced,
            "Speech detection strictness. Permissive (ratio 0.20) keeps almost all voice but lets through more noise. " +
            "Balanced (0.40) is the default best-F1 setting. Strict (0.60) filters more aggressively but may drop quiet voice.");
#endif
    }

    private void PreventDelete()
    {
        // Prevent the plugin from being deleted
        gameObject.transform.parent = null;
        gameObject.hideFlags = HideFlags.HideAndDontSave;
    }
}
