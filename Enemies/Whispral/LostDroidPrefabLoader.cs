using System;
using System.Collections;
using System.IO;
using System.Reflection;
using REPOLib.Objects.Sdk;
using UnityEngine;
using VepMod.VepFramework;
using Object = UnityEngine.Object;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Charge le prefab LostDroid depuis l'AssetBundle de WesleysEnemies.
///     Supporte le chargement asynchrone pour éviter les freezes.
/// </summary>
public static class LostDroidPrefabLoader
{
    private static readonly VepLogger LOG = VepLogger.Create("LostDroidPrefabLoader");

    private static AssetBundle _wesleysBundle;

    /// <summary>
    ///     Le prefab LostDroid chargé, ou null si non disponible.
    /// </summary>
    public static GameObject LostDroidPrefab { get; private set; }

    /// <summary>
    ///     Indique si le prefab est disponible.
    /// </summary>
    public static bool IsAvailable => LostDroidPrefab != null;

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
        LOG.Info("Starting async preload of LostDroid prefab...");

        // Chercher le chemin du bundle
        var bundlePath = FindWesleysBundlePath();
        if (string.IsNullOrEmpty(bundlePath))
        {
            LOG.Warning("WesleysEnemies AssetBundle not found - LostDroid hallucinations disabled");
            FinishLoading(false, onComplete);
            yield break;
        }

        // Charger l'AssetBundle de manière asynchrone
        var bundleRequest = AssetBundle.LoadFromFileAsync(bundlePath);
        yield return bundleRequest;

        _wesleysBundle = bundleRequest.assetBundle;
        if (_wesleysBundle == null)
        {
            LOG.Error($"Failed to load AssetBundle from {bundlePath}");
            FinishLoading(false, onComplete);
            yield break;
        }

        // Essayer de charger le prefab directement
        var assetRequest = _wesleysBundle.LoadAssetAsync<GameObject>("LostDroid");
        yield return assetRequest;

        if (assetRequest.asset != null)
        {
            LostDroidPrefab = assetRequest.asset as GameObject;
            LOG.Info($"LostDroid prefab loaded directly from bundle: {bundlePath}");
            FinishLoading(true, onComplete);
            yield break;
        }

        // Méthode 2: Chercher dans tous les assets
        var allAssetNames = _wesleysBundle.GetAllAssetNames();
        LOG.Debug($"Bundle contains {allAssetNames.Length} assets");

        foreach (var assetName in allAssetNames)
        {
            if (assetName.ToLower().Contains("lostdroid") && assetName.EndsWith(".prefab"))
            {
                var prefabRequest = _wesleysBundle.LoadAssetAsync<GameObject>(assetName);
                yield return prefabRequest;

                if (prefabRequest.asset != null)
                {
                    LostDroidPrefab = prefabRequest.asset as GameObject;
                    LOG.Info($"LostDroid prefab loaded from: {assetName}");
                    FinishLoading(true, onComplete);
                    yield break;
                }
            }
        }

        // Méthode 3: Via EnemyContent
        var contentRequest = _wesleysBundle.LoadAssetAsync<EnemyContent>("EnemyContentLostDroid");
        yield return contentRequest;

        if (contentRequest.asset is EnemyContent enemyContent)
        {
            LostDroidPrefab = GetPrefabFromContent(enemyContent);
            if (LostDroidPrefab != null)
            {
                LOG.Info($"LostDroid prefab loaded via EnemyContent from {bundlePath}");
                FinishLoading(true, onComplete);
                yield break;
            }
        }

        LOG.Error("Failed to load LostDroid prefab from bundle - all methods exhausted");
        FinishLoading(false, onComplete);
    }

    private static void FinishLoading(bool success, Action<bool>? onComplete)
    {
        IsLoading = false;
        LoadCompleted = true;
        onComplete?.Invoke(success);
    }

    private static GameObject GetPrefabFromContent(EnemyContent content)
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

    private static string FindWesleysBundlePath()
    {
        const string bundleFileName = "wesleysenemies_enemyprefabs";

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
                    var files = Directory.GetFiles(pluginsDir, bundleFileName, SearchOption.AllDirectories);
                    if (files.Length > 0) return files[0];
                }

                var localFiles = Directory.GetFiles(assemblyDir, bundleFileName, SearchOption.AllDirectories);
                if (localFiles.Length > 0) return localFiles[0];
            }
        }

        // 2. Chercher dans BepInEx/plugins
        var bepInExPath = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins");
        if (Directory.Exists(bepInExPath))
        {
            var files = Directory.GetFiles(bepInExPath, bundleFileName, SearchOption.AllDirectories);
            if (files.Length > 0) return files[0];
        }

        // 3. Fallback
        var parentPath = Path.Combine(Application.dataPath, "..");
        if (Directory.Exists(parentPath))
        {
            var allFiles = Directory.GetFiles(parentPath, bundleFileName, SearchOption.AllDirectories);
            if (allFiles.Length > 0) return allFiles[0];
        }

        return null;
    }

    /// <summary>
    ///     Libère les ressources chargées.
    /// </summary>
    public static void Unload()
    {
        if (_wesleysBundle != null)
        {
            _wesleysBundle.Unload(false);
            _wesleysBundle = null;
        }

        LostDroidPrefab = null;
        IsLoading = false;
        LoadCompleted = false;
    }
}