#nullable disable
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using VepMod.VepFramework;

namespace VepMod.Patchs;

[HarmonyPatch(typeof(EnemyDirector))]
internal class EnemyDirectorStartPatch
{
    private static readonly VepLogger LOG = VepLogger.Create<EnemyDirectorStartPatch>(debugEnabled: false);
    private static HashSet<string> _filterEnemies;
    private static bool _setupComplete;
    private static ConfigFile _configFile;

    public static void Initialize(ConfigFile config)
    {
        _configFile = config;
        LOG.Info("EnemyDirectorStartPatch initialized with ConfigFile.");
    }

    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    public static void SetupEnemies(EnemyDirector __instance)
    {
        if (_setupComplete) return;

        var enemySetupListArray = new List<EnemySetup>[3]
        {
            __instance.enemiesDifficulty1,
            __instance.enemiesDifficulty2,
            __instance.enemiesDifficulty3
        };
        _filterEnemies = new HashSet<string>();
        foreach (var enemySetupList in enemySetupListArray)
        {
            foreach (var enemySetup in enemySetupList)
            {
                if (enemySetup.spawnObjects[0].Prefab.name.Contains("Director"))
                {
                    _filterEnemies.Add(enemySetup.spawnObjects[1].Prefab.name);
                }
                else
                {
                    _filterEnemies.Add(enemySetup.spawnObjects[0].Prefab.name);
                }
            }
        }

        _setupComplete = true;
        SetupEnemyConfig();
        LOG.Info("Enemy setup complete.");
    }

    private static void SetupEnemyConfig()
    {
        LOG.Info("Setting up enemy config...");
        foreach (var filterEnemy in _filterEnemies)
        {
            filterEnemy.Replace("Enemy - ", "");
            VepMod.EnemyConfigEntries[filterEnemy] = _configFile.Bind("Enemies", filterEnemy, true,
                "Enables/disables ability for " + filterEnemy + " to mimic player voices.");
            LOG.Debug("Added config entry for enemy: " + filterEnemy);
        }
    }
}