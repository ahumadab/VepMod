using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
#if DEBUG
using VepMod.Dev;
#endif
using VepMod.Enemies.Whispral;
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
    public static readonly RangeValue<int> SamplingRate = RangeValue.Int(16000, 48000, 48000);

    // Paires min/max liées - corrigent automatiquement si min > max
    public static readonly MinMaxRange<float> ShareDelay = MinMaxRange.Float(
        10f, 60f, 10f,
        10f, 120f, 30f);

    public static readonly MinMaxRange<float> VoiceDelay = MinMaxRange.Float(
        6f, 30f, 8f,
        6f, 30f, 15f);

    // Durée minimale d'enregistrement (garde-fou avant VAD)
    public static readonly RangeValue<float> AudioMinDuration = RangeValue.Float(0.1f, 2f, 0.3f);
}

[BepInPlugin("com.vep.vepMod", "VepMod", "1.1.0")]
[BepInDependency("REPOLib")]
public class VepMod : BaseUnityPlugin
{
    private static Harmony _harmony;
    public static ConfigEntry<float> ConfigVoiceVolume;
    public static ConfigEntry<int> ConfigSamplingRate;

    // Boucle 1 : Partage des sons entre clients
    public static BoundMinMaxRange<float> ShareDelay;
    public static ConfigEntry<int> ConfigSamplesPerPlayer;

    // Boucle 2 : Commandes de lecture (pendant debuff)
    public static BoundMinMaxRange<float> VoiceDelay;
    public static ConfigEntry<bool> ConfigVoiceFilterEnabled;

#if DEBUG
    // Outils de développement (build dev uniquement, désactivés par défaut)
    public static ConfigEntry<bool> ConfigEnableDevTools;
#endif

    // Validation audio (VAD)
    public static ConfigEntry<float> ConfigAudioMinDuration;
#if !VEPMOD_NO_VAD
    public static ConfigEntry<bool> ConfigVadEnabled;
    public static ConfigEntry<VadSensitivity> ConfigVadSensitivity;
#endif

    public static readonly Dictionary<string, ConfigEntry<bool>> EnemyConfigEntries = new();

    internal Harmony? Harmony { get; set; }

    private void Awake()
    {
        _harmony = new Harmony("VepMod.Plugin");
        PreventDelete();

        InitConfig();

        _harmony.PatchAll();
        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");

#if DEBUG
        if (ConfigEnableDevTools.Value)
        {
            gameObject.AddComponent<DroidDevTools>();
            Logger.LogInfo("VepMod dev tools enabled (F7 spawn / F8 teleport / F9 drop / F10 HUD / F11 despawn).");
        }
#endif

        // Pr�charger le prefab LostDroid de mani�re asynchrone pour �viter les freezes
        StartCoroutine(DroidPrefabLoader.PreloadAsync(success =>
        {
            if (success)
            {
                Logger.LogInfo("LostDroid prefab preloaded successfully.");
            }
            else
            {
                Logger.LogWarning("LostDroid prefab preload failed - hallucinations may not work.");
            }
        }));
    }

    public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Logger.LogInfo($"Scene loaded: {scene.name}");
        // Code that runs when a new scene is loaded goes here
    }

    private void InitConfig()
    {
        Logger.LogInfo("Initializing config for VepMod...");

        // General settings
        ConfigVoiceVolume = Config.BindRange("General", "Volume", ConfigRanges.Volume, "Volume of the mimic voices.");

        // Boucle 1 : Partage des sons entre clients
        ShareDelay = Config.BindMinMax("Audio Sharing",
            "Share Min Delay", "Share Max Delay", ConfigRanges.ShareDelay,
            "Minimum time between sharing audio clips with other players.",
            "Maximum time between sharing audio clips with other players.");
        ConfigSamplesPerPlayer = Config.BindRange("Audio Sharing", "Samples Per Player", ConfigRanges.SamplesPerPlayer,
            "Maximum number of audio samples stored per player.");

        // Boucle 2 : Commandes de lecture (pendant debuff Whispral)
        VoiceDelay = Config.BindMinMax("Voice Playback",
            "Voice Min Delay", "Voice Max Delay", ConfigRanges.VoiceDelay,
            "Minimum time between voice playback commands during Whispral debuff.",
            "Maximum time between voice playback commands during Whispral debuff.");
        ConfigVoiceFilterEnabled = Config.Bind("Voice Playback", "Voice Filter Enabled?", false,
            new ConfigDescription(
                "Turning this will enable modifiable filters for which enemies can play back voices during the Whispral debuff."));

        // Experimental settings
        ConfigSamplingRate = Config.BindRange("Experimental", "Sampling Rate", ConfigRanges.SamplingRate,
            "Only change this value if the console gives you a warning about your microphone frequency not being supported.");

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

    internal void Unpatch()
    {
        _harmony.UnpatchSelf();
    }
}
