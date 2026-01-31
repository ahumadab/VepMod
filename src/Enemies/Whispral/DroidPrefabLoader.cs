using System;
using System.Collections;
using System.IO;
using System.Reflection;
using REPOLib.Objects.Sdk;
using UnityEngine;
using VepMod.VepFramework;
using Object = UnityEngine.Object;

// ReSharper disable Unity.NoNullPatternMatching

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Charge le prefab du Droid
///     Supporte le chargement asynchrone pour éviter les freezes.
/// </summary>
public static class DroidPrefabLoader
{
    private const string BundleFileName = "vepmod_prefabs";
    private const string DroidPrefabName = "MyDroid";
    private const string EmbeddedBundleResourceName = "VepMod.Resources.vepmod_prefabs";
    private static readonly VepLogger LOG = VepLogger.Create(nameof(DroidPrefabLoader));

    private static AssetBundle? _bundle;

    /// <summary>
    ///     Le prefab Droid chargé, ou null si non disponible.
    /// </summary>
    public static GameObject? DroidPrefab { get; private set; }

    /// <summary>
    ///     Indique si le prefab est disponible.
    /// </summary>
    public static bool IsAvailable => DroidPrefab != null;

    /// <summary>
    ///     Indique si le chargement est en cours.
    /// </summary>
    public static bool IsLoading { get; private set; }

    /// <summary>
    ///     Indique si le chargement est terminé (succès ou échec).
    /// </summary>
    public static bool LoadCompleted { get; private set; }

    /// <summary>
    ///     Démarre le préchargement asynchrone du prefab.
    ///     Doit être appelé au démarrage du mod via une coroutine.
    /// </summary>
    public static IEnumerator PreloadAsync(Action<bool>? onComplete = null)
    {
        if (LoadCompleted || IsLoading)
        {
            onComplete?.Invoke(IsAvailable);
            yield break;
        }

        IsLoading = true;
        LOG.Info("Starting async preload of Droid prefab...");

        // 1. Essayer de charger depuis la ressource embarquée dans la DLL
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

        // 2. Fallback: charger depuis le disque
        if (!embeddedLoaded)
        {
            var bundlePath = FindBundlePath();
            if (string.IsNullOrEmpty(bundlePath))
            {
                LOG.Warning("VepMod AssetBundle not found - Droid hallucinations disabled");
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

        // Essayer de charger le prefab directement
        var assetRequest = _bundle.LoadAssetAsync<GameObject>(DroidPrefabName);
        yield return assetRequest;

        if (assetRequest.asset != null)
        {
            DroidPrefab = assetRequest.asset as GameObject;
            LOG.Info("Droid prefab loaded directly from bundle");
            FinishLoading(true, onComplete);
            yield break;
        }

        // Méthode 2: Chercher dans tous les assets
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
                    LOG.Info($"Droid prefab loaded from: {assetName}");
                    FinishLoading(true, onComplete);
                    yield break;
                }
            }
        }

        LOG.Error("Failed to load Droid prefab from bundle - all methods exhausted");
        FinishLoading(false, onComplete);
    }

    private static void FinishLoading(bool success, Action<bool>? onComplete)
    {
        IsLoading = false;
        LoadCompleted = true;
        onComplete?.Invoke(success);
    }

    private static GameObject? GetPrefabFromContent(EnemyContent content)
    {
        var type = content.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.PropertyType == typeof(GameObject) || prop.PropertyType.IsSubclassOf(typeof(Object)))
            {
                try
                {
                    if (prop.GetValue(content) is GameObject go && go != null)
                    {
                        return go;
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }

        foreach (var field in type.GetFields(flags))
        {
            if (field.FieldType == typeof(GameObject) || field.FieldType.IsSubclassOf(typeof(Object)))
            {
                try
                {
                    if (field.GetValue(content) is GameObject go && go != null)
                    {
                        return go;
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }

        return null;
    }

    private static string? FindBundlePath()
    {
        // 1. Chercher à partir du dossier de notre DLL
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

        // 2. Chercher dans BepInEx/plugins
        var bepInExPath = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins");
        if (Directory.Exists(bepInExPath))
        {
            var files = Directory.GetFiles(bepInExPath, BundleFileName, SearchOption.AllDirectories);
            if (files.Length > 0) return files[0];
        }

        // 3. Fallback
        var parentPath = Path.Combine(Application.dataPath, "..");
        if (Directory.Exists(parentPath))
        {
            var allFiles = Directory.GetFiles(parentPath, BundleFileName, SearchOption.AllDirectories);
            if (allFiles.Length > 0) return allFiles[0];
        }

        return null;
    }

    /// <summary>
    ///     Libère les ressources chargées.
    /// </summary>
    public static void Unload()
    {
        if (_bundle != null)
        {
            _bundle.Unload(false);
            _bundle = null;
        }

        DroidPrefab = null;
        IsLoading = false;
        LoadCompleted = false;
    }
}
