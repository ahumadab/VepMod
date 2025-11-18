using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using REPOLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VepMod;

[BepInPlugin("Vep.VepMod", "VepMod", "0.0.3")]
[BepInDependency(MyPluginInfo.PLUGIN_GUID)]
public class VepMod : BaseUnityPlugin
{
    public static VepMod Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony? Harmony { get; set; }

    private void Awake()
    {
        Instance = this;

        // Prevent the plugin from being deleted
        gameObject.transform.parent = null;
        gameObject.hideFlags = HideFlags.HideAndDontSave;

        Patch();

        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
        InitMod();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Update()
    {
        // Code that runs every frame goes here
    }

    public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Logger.LogInfo($"Scene loaded: {scene.name}");
        // Code that runs when a new scene is loaded goes here
    }

    private void InitMod()
    {
        Logger.LogDebug("Initializing VepMod...");
        AssetsManager.Instance.RegisterAssets();
    }

    internal void Patch()
    {
        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();
    }

    internal void Unpatch()
    {
        Harmony?.UnpatchSelf();
    }
}