using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Corrige les shaders des materials pour les prefabs chargés depuis un AssetBundle.
///     Récupère les shaders manquants depuis les objets du jeu au runtime.
/// </summary>
public static class DroidMaterialFixer
{
    private const string HurtableShaderName = "Hurtable/Hurtable";
    private static readonly VepLogger LOG = VepLogger.Create(nameof(DroidMaterialFixer));

    // Cache du shader pour éviter de le rechercher à chaque instantiation
    private static Shader? _cachedShader;

    /// <summary>
    ///     Corrige les materials d'un GameObject avec le shader Hurtable.
    /// </summary>
    /// <returns>Nombre de materials corrigés</returns>
    public static int FixMaterials(GameObject target)
    {
        var shader = GetHurtableShader();
        if (shader == null)
        {
            LOG.Warning($"Shader '{HurtableShaderName}' not found - materials may not display correctly");
            return 0;
        }

        return ApplyShader(target, shader);
    }

    private static int ApplyShader(GameObject target, Shader shader)
    {
        var fixedCount = 0;
        foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.materials)
            {
                if (NeedsShaderFix(material))
                {
                    material.shader = shader;
                    fixedCount++;
                }
            }
        }

        if (fixedCount > 0)
        {
            LOG.Info($"Fixed {fixedCount} materials with shader '{HurtableShaderName}'");
        }

        return fixedCount;
    }

    /// <summary>
    ///     Invalide le cache du shader (utile si les PlayerAvatars changent).
    /// </summary>
    public static void ClearCache()
    {
        _cachedShader = null;
    }

    private static bool NeedsShaderFix(Material material)
    {
        if (material == null) return false;
        if (material.shader == null) return true;
        if (material.shader.name == "Hidden/InternalErrorShader") return true;
        if (material.shader.name != HurtableShaderName) return true;
        return false;
    }

    private static Shader? GetHurtableShader()
    {
        if (_cachedShader != null) return _cachedShader;
        _cachedShader = Shader.Find(HurtableShaderName);
        if (_cachedShader != null) return _cachedShader;
        _cachedShader = FindShaderInPlayerAvatars(HurtableShaderName);
        return _cachedShader;
    }

    private static Shader? FindShaderInPlayerAvatars(string shaderName)
    {
        var allVisuals = Object.FindObjectsOfType<PlayerAvatarVisuals>(true);
        foreach (var visuals in allVisuals)
        {
            foreach (var renderer in visuals.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null && material.shader != null &&
                        material.shader.name == shaderName)
                    {
                        return material.shader;
                    }
                }
            }
        }

        return null;
    }
}