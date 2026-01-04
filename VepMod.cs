using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using VepMod.Enemies.Whispral;
using VepMod.Patchs;

namespace VepMod;

[BepInPlugin("com.vep.vepMod", "VepMod", "1.0.0")]
[BepInDependency("REPOLib")]
public class VepMod : BaseUnityPlugin
{
    private static Harmony _harmony;
    public static ConfigEntry<float> ConfigVoiceVolume;
    public static ConfigEntry<bool> ConfigHearYourself;
    public static ConfigEntry<bool> ConfigEnemyFilterEnabled;
    public static ConfigEntry<int> ConfigSamplingRate;

    // Boucle 1 : Partage des sons entre clients
    public static ConfigEntry<float> ConfigShareMinDelay;
    public static ConfigEntry<float> ConfigShareMaxDelay;
    public static ConfigEntry<int> ConfigSamplesPerPlayer;

    // Boucle 2 : Commandes de lecture (pendant debuff)
    public static ConfigEntry<float> ConfigVoiceMinDelay;
    public static ConfigEntry<float> ConfigVoiceMaxDelay;
    public static ConfigEntry<bool> ConfigVoiceFilterEnabled;

    public static Dictionary<string, ConfigEntry<bool>> EnemyConfigEntries = new();

    internal Harmony? Harmony { get; set; }

    private void Awake()
    {
        _harmony = new Harmony("VepMod.Plugin");
        PreventDelete();

        InitConfig();

        _harmony.PatchAll();
        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");

        // Précharger le prefab LostDroid de manière asynchrone pour éviter les freezes
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
        ConfigVoiceVolume = Config.Bind("General", "Volume", 0.5f,
            new ConfigDescription("Volume of the mimic voices.", new AcceptableValueRange<float>(0.0f, 1f)));
        ConfigHearYourself = Config.Bind("General", "Hear Yourself?", false,
            new ConfigDescription("Turning this off will make it so you won't hear your own voice played by mimics."));

        // Boucle 1 : Partage des sons entre clients
        ConfigShareMinDelay = Config.Bind("Audio Sharing", "Share Min Delay", 10f,
            new ConfigDescription("Minimum time between sharing audio clips with other players.",
                new AcceptableValueRange<float>(10f, 60f)));
        ConfigShareMaxDelay = Config.Bind("Audio Sharing", "Share Max Delay", 30f,
            new ConfigDescription("Maximum time between sharing audio clips with other players.",
                new AcceptableValueRange<float>(30f, 120f)));
        ConfigSamplesPerPlayer = Config.Bind("Audio Sharing", "Samples Per Player", 10,
            new ConfigDescription("Maximum number of audio samples stored per player.",
                new AcceptableValueRange<int>(5, 20)));

        // Boucle 2 : Commandes de lecture (pendant debuff Whispral)
        ConfigVoiceMinDelay = Config.Bind("Voice Playback", "Voice Min Delay", 8f,
            new ConfigDescription("Minimum time between voice playback commands during Whispral debuff.",
                new AcceptableValueRange<float>(6f, 15f)));
        ConfigVoiceMaxDelay = Config.Bind("Voice Playback", "Voice Max Delay", 15f,
            new ConfigDescription("Maximum time between voice playback commands during Whispral debuff.",
                new AcceptableValueRange<float>(6f, 30f)));
        ConfigVoiceFilterEnabled = Config.Bind("Voice Playback", "Voice Filter Enabled?", false,
            new ConfigDescription(
                "Turning this will enable modifiable filters for which enemies can play back voices during the Whispral debuff."));

        // Experimental settings
        ConfigSamplingRate = Config.Bind("Experimental", "Sampling Rate", 48000,
            new ConfigDescription(
                "Only change this value if the console gives you a warning about your microphone frequency not being supported.",
                new AcceptableValueRange<int>(16000, 48000)));

        // Filter settings
        ConfigEnemyFilterEnabled = Config.Bind("Filter", "Filter Enabled?", false,
            "Turning this on allows you to customize which enemies can mimic voices. (Keep as 'false' if you want to allow custom enemies to mimic voices)");

        EnemyDirectorStartPatch.Initialize(Config);
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