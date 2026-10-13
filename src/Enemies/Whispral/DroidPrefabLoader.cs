using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

public static class DroidPrefabLoader
{
    private const string BundleFileName = "vepmod_prefabs";
    private const string DroidPrefabName = "MyDroid";
    private const string EmbeddedBundleResourceName = "VepMod.Resources.vepmod_prefabs";
    private static readonly VepLogger LOG = VepLogger.Create(nameof(DroidPrefabLoader));

    private static AssetBundle? _bundle;

    public static GameObject? DroidPrefab { get; private set; }

    public static bool IsAvailable => DroidPrefab != null;

    public static bool IsLoading { get; private set; }

    /// <summary>
    ///     True once loading has ended, successfully or not.
    /// </summary>
    public static bool LoadCompleted { get; private set; }

    public static IEnumerator PreloadAsync(Action<bool>? onComplete = null)
    {
        if (LoadCompleted || IsLoading)
        {
            onComplete?.Invoke(IsAvailable);
            yield break;
        }

        IsLoading = true;
        LOG.Info("Starting async preload of Hallucination prefab...");

        var embeddedLoaded = false;
        using (var stream = typeof(DroidPrefabLoader).Assembly.GetManifestResourceStream(EmbeddedBundleResourceName))
        {
            if (stream != null)
            {
                LOG.Info("Found embedded AssetBundle resource, loading from DLL...");
                byte[] bytes;
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    bytes = ms.ToArray();
                }

                var bundleRequest = AssetBundle.LoadFromMemoryAsync(bytes);
                yield return bundleRequest;

                _bundle = bundleRequest.assetBundle;
                if (_bundle != null)
                {
                    LOG.Info("AssetBundle loaded successfully from embedded resource");
                    embeddedLoaded = true;
                }
                else
                {
                    LOG.Warning("Failed to load AssetBundle from embedded resource, falling back to file...");
                }
            }
            else
            {
                LOG.Debug("No embedded AssetBundle found, falling back to file...");
            }
        }

        if (!embeddedLoaded)
        {
            var bundlePath = FindBundlePath();
            if (string.IsNullOrEmpty(bundlePath))
            {
                LOG.Warning("VepMod AssetBundle not found - Hallucinations disabled");
                FinishLoading(false, onComplete);
                yield break;
            }

            var bundleRequest = AssetBundle.LoadFromFileAsync(bundlePath);
            yield return bundleRequest;

            _bundle = bundleRequest.assetBundle;
            if (_bundle == null)
            {
                LOG.Error($"Failed to load AssetBundle from {bundlePath}");
                FinishLoading(false, onComplete);
                yield break;
            }

            LOG.Info($"AssetBundle loaded from file: {bundlePath}");
        }

        var assetRequest = _bundle.LoadAssetAsync<GameObject>(DroidPrefabName);
        yield return assetRequest;

        if (assetRequest.asset != null)
        {
            DroidPrefab = assetRequest.asset as GameObject;
            LOG.Info("Hallucination prefab loaded directly from bundle");
            FinishLoading(true, onComplete);
            yield break;
        }

        var allAssetNames = _bundle.GetAllAssetNames();
        LOG.Debug($"Bundle contains {allAssetNames.Length} assets");

        foreach (var assetName in allAssetNames)
        {
            if (assetName.ToLower().Contains(DroidPrefabName.ToLower()) && assetName.EndsWith(".prefab"))
            {
                var prefabRequest = _bundle.LoadAssetAsync<GameObject>(assetName);
                yield return prefabRequest;

                if (prefabRequest.asset != null)
                {
                    DroidPrefab = prefabRequest.asset as GameObject;
                    LOG.Info($"Hallucination prefab loaded from: {assetName}");
                    FinishLoading(true, onComplete);
                    yield break;
                }
            }
        }

        LOG.Error("Failed to load Hallucination prefab from bundle - all methods exhausted");
        FinishLoading(false, onComplete);
    }

    private static void FinishLoading(bool success, Action<bool>? onComplete)
    {
        IsLoading = false;
        LoadCompleted = true;
        onComplete?.Invoke(success);
    }

    private static string? FindBundlePath()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            var assemblyDir = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                var pluginsDir = Path.GetDirectoryName(assemblyDir);
                if (pluginsDir != null && Directory.Exists(pluginsDir))
                {
                    var files = Directory.GetFiles(pluginsDir, BundleFileName, SearchOption.AllDirectories);
                    if (files.Length > 0) return files[0];
                }

                var localFiles = Directory.GetFiles(assemblyDir, BundleFileName, SearchOption.AllDirectories);
                if (localFiles.Length > 0) return localFiles[0];
            }
        }

        var bepInExPath = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins");
        if (Directory.Exists(bepInExPath))
        {
            var files = Directory.GetFiles(bepInExPath, BundleFileName, SearchOption.AllDirectories);
            if (files.Length > 0) return files[0];
        }

        var parentPath = Path.Combine(Application.dataPath, "..");
        if (Directory.Exists(parentPath))
        {
            var allFiles = Directory.GetFiles(parentPath, BundleFileName, SearchOption.AllDirectories);
            if (allFiles.Length > 0) return allFiles[0];
        }

        return null;
    }
}
