using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using REPOLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using VepMod.Scripts.Patchs;

namespace VepMod;

[BepInPlugin("com.vep.vepMod", "VepMod", "0.1.0")]
[BepInDependency(MyPluginInfo.PLUGIN_GUID)]
public class VepMod : BaseUnityPlugin
{
    private static Harmony _harmony;
    public static ConfigEntry<float> ConfigVoiceVolume;
    public static ConfigEntry<float> ConfigMinDelay;
    public static ConfigEntry<float> ConfigMaxDelay;
    public static ConfigEntry<bool> ConfigHearYourself;
    public static ConfigEntry<bool> ConfigFilterEnabled;
    public static ConfigEntry<int> ConfigSamplingRate;
    public static Dictionary<string, ConfigEntry<bool>> EnemyConfigEntries = new();

    internal Harmony? Harmony { get; set; }

    private void Awake()
    {
        _harmony = new Harmony("VepMod.Plugin");
        PreventDelete();

        InitConfig();

        _harmony.PatchAll();
        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
    }

    public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Logger.LogInfo($"Scene loaded: {scene.name}");
        // Code that runs when a new scene is loaded goes here
    }

    private void InitConfig()
    {
        Logger.LogInfo("Initializing config for VepMod...");
        ConfigVoiceVolume = Config.Bind("General", "Volume", 0.75f,
            new ConfigDescription("Volume of the mimic voices.", new AcceptableValueRange<float>(0.0f, 1f)));
        ConfigMinDelay = Config.Bind("General", "MinDelay", 30f,
            new ConfigDescription("Minimum time before an audio clip is recorded and played.",
                new AcceptableValueRange<float>(30f, 120f)));
        ConfigMaxDelay = Config.Bind("General", "MaxDelay", 120f,
            new ConfigDescription("Maximum time before an audio clip is recorded and played.",
                new AcceptableValueRange<float>(60f, 240f)));
        ConfigHearYourself = Config.Bind("General", "Hear Yourself?", false,
            new ConfigDescription("Turning this off will make it so you won't hear your own voice played by mimics."));
        ConfigSamplingRate = Config.Bind("Experimental", "Sampling Rate", 48000,
            new ConfigDescription(
                "Only change this value if the console gives you a warning about your microphone frequency not being supported.",
                new AcceptableValueRange<int>(16000, 48000)));
        ConfigFilterEnabled = Config.Bind("Filter", "Filter Enabled?", false,
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