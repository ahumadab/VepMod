using System.IO;
using System.Reflection;
using REPOLib.Objects.Sdk;
using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Charge le prefab LostDroid depuis l'AssetBundle de WesleysEnemies.
/// </summary>
public static class LostDroidPrefabLoader
{
    private static readonly VepLogger LOG = VepLogger.Create("LostDroidPrefabLoader", debugEnabled: true);

    private static AssetBundle _wesleysBundle;
    private static GameObject _lostDroidPrefab;
    private static bool _loadAttempted;

    /// <summary>
    ///     Le prefab LostDroid chargé, ou null si non disponible.
    /// </summary>
    public static GameObject LostDroidPrefab
    {
        get
        {
            if (!_loadAttempted)
            {
                TryLoadPrefab();
            }
            return _lostDroidPrefab;
        }
    }

    /// <summary>
    ///     Indique si le prefab est disponible.
    /// </summary>
    public static bool IsAvailable => LostDroidPrefab != null;

    private static void TryLoadPrefab()
    {
        _loadAttempted = true;

        // Chercher l'AssetBundle de WesleysEnemies
        var bundlePath = FindWesleysBundlePath();
        if (string.IsNullOrEmpty(bundlePath))
        {
            LOG.Warning("WesleysEnemies AssetBundle not found - LostDroid hallucinations disabled");
            return;
        }

        // Charger l'AssetBundle
        _wesleysBundle = AssetBundle.LoadFromFile(bundlePath);
        if (_wesleysBundle == null)
        {
            LOG.Error($"Failed to load AssetBundle from {bundlePath}");
            return;
        }

        // Essayer plusieurs méthodes pour charger le prefab

        // Méthode 1: Charger directement le prefab par son nom
        _lostDroidPrefab = _wesleysBundle.LoadAsset<GameObject>("LostDroid");
        if (_lostDroidPrefab != null)
        {
            LOG.Info($"LostDroid prefab loaded directly from bundle: {bundlePath}");
            return;
        }

        // Méthode 2: Lister tous les assets et trouver le prefab
        var allAssetNames = _wesleysBundle.GetAllAssetNames();
        LOG.Debug($"Bundle contains {allAssetNames.Length} assets");
        foreach (var assetName in allAssetNames)
        {
            LOG.Debug($"  Asset: {assetName}");
            if (assetName.ToLower().Contains("lostdroid") && assetName.EndsWith(".prefab"))
            {
                _lostDroidPrefab = _wesleysBundle.LoadAsset<GameObject>(assetName);
                if (_lostDroidPrefab != null)
                {
                    LOG.Info($"LostDroid prefab loaded from: {assetName}");
                    return;
                }
            }
        }

        // Méthode 3: Via EnemyContent
        var enemyContent = _wesleysBundle.LoadAsset<EnemyContent>("EnemyContentLostDroid");
        if (enemyContent != null)
        {
            _lostDroidPrefab = GetPrefabFromContent(enemyContent);
            if (_lostDroidPrefab != null)
            {
                LOG.Info($"LostDroid prefab loaded via EnemyContent from {bundlePath}");
                return;
            }
        }

        LOG.Error("Failed to load LostDroid prefab from bundle - all methods exhausted");
    }

    private static GameObject GetPrefabFromContent(EnemyContent content)
    {
        // Essayer les noms de propriétés/champs courants
        var type = content.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        LOG.Debug($"Inspecting EnemyContent type: {type.FullName}");

        // Chercher une propriété ou un champ de type GameObject
        foreach (var prop in type.GetProperties(flags))
        {
            LOG.Debug($"  Property: {prop.Name} ({prop.PropertyType.Name})");
            if (prop.PropertyType == typeof(GameObject) || prop.PropertyType.IsSubclassOf(typeof(UnityEngine.Object)))
            {
                try
                {
                    var value = prop.GetValue(content);
                    if (value is GameObject go && go != null)
                    {
                        LOG.Debug($"Found prefab via property: {prop.Name}");
                        return go;
                    }
                }
                catch (System.Exception e)
                {
                    LOG.Debug($"  Error reading property {prop.Name}: {e.Message}");
                }
            }
        }

        foreach (var field in type.GetFields(flags))
        {
            LOG.Debug($"  Field: {field.Name} ({field.FieldType.Name})");
            if (field.FieldType == typeof(GameObject) || field.FieldType.IsSubclassOf(typeof(UnityEngine.Object)))
            {
                try
                {
                    var value = field.GetValue(content);
                    if (value is GameObject go && go != null)
                    {
                        LOG.Debug($"Found prefab via field: {field.Name}");
                        return go;
                    }
                }
                catch (System.Exception e)
                {
                    LOG.Debug($"  Error reading field {field.Name}: {e.Message}");
                }
            }
        }

        return null;
    }

    private static string FindWesleysBundlePath()
    {
        // Nom du fichier AssetBundle
        const string bundleFileName = "wesleysenemies_enemyprefabs";

        // 1. Chercher à partir du dossier de notre DLL (fonctionne avec Thunderstore Mod Manager)
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            var assemblyDir = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                // Remonter jusqu'au dossier plugins (notre DLL est dans plugins/Unknown-VepMod/)
                var pluginsDir = Path.GetDirectoryName(assemblyDir);
                if (pluginsDir != null && Directory.Exists(pluginsDir))
                {
                    LOG.Debug($"Searching for bundle in plugins dir: {pluginsDir}");
                    var files = Directory.GetFiles(pluginsDir, bundleFileName, SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        LOG.Debug($"Found WesleysEnemies bundle at: {files[0]}");
                        return files[0];
                    }
                }

                // Chercher aussi directement dans le dossier de l'assembly
                LOG.Debug($"Searching for bundle in assembly dir: {assemblyDir}");
                var localFiles = Directory.GetFiles(assemblyDir, bundleFileName, SearchOption.AllDirectories);
                if (localFiles.Length > 0)
                {
                    LOG.Debug($"Found WesleysEnemies bundle at: {localFiles[0]}");
                    return localFiles[0];
                }
            }
        }

        // 2. Chercher dans les dossiers de plugins BepInEx relatif au jeu
        var bepInExPath = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins");
        if (Directory.Exists(bepInExPath))
        {
            LOG.Debug($"Searching for bundle in game BepInEx: {bepInExPath}");
            var files = Directory.GetFiles(bepInExPath, bundleFileName, SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                LOG.Debug($"Found WesleysEnemies bundle at: {files[0]}");
                return files[0];
            }
        }

        // 3. Chercher dans le dossier parent du jeu (fallback)
        var parentPath = Path.Combine(Application.dataPath, "..");
        if (Directory.Exists(parentPath))
        {
            var allFiles = Directory.GetFiles(parentPath, bundleFileName, SearchOption.AllDirectories);
            if (allFiles.Length > 0)
            {
                LOG.Debug($"Found WesleysEnemies bundle at: {allFiles[0]}");
                return allFiles[0];
            }
        }

        LOG.Warning("Bundle search paths exhausted, WesleysEnemies not found");
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
        _lostDroidPrefab = null;
        _loadAttempted = false;
    }
}
