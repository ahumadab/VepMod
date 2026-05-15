using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Applique les couleurs cosmétiques d'un PlayerAvatar source aux renderers
///     d'un droid hallucination. Reproduit le comportement de PlayerCosmetics.SetupColorsLogic
///     (par body part) sur une hiérarchie qui ne possède pas de PlayerMaterial.
/// </summary>
internal static class DroidCosmeticPainter
{
    private static readonly VepLogger LOG = VepLogger.Create(nameof(DroidCosmeticPainter));
    private static readonly int AlbedoColorKey = Shader.PropertyToID("_AlbedoColor");

    public static void Apply(GameObject droidRoot, PlayerAvatar sourcePlayer)
    {
        if (droidRoot == null || sourcePlayer == null)
        {
            LOG.Debug("Null check failed for droid root or source player");
            LOG.Debug($"droidRoot: {(droidRoot != null ? "valid" : "null")}");
            LOG.Debug($"sourcePlayer: {(sourcePlayer != null ? "valid" : "null")}");
            LOG.Warning("Droid root or source player is null, skipping cosmetic application");
            return;
        }

        var cosmetics = sourcePlayer.playerCosmetics;
        var colorsEquipped = cosmetics != null ? cosmetics.colorsEquipped : null;
        if (colorsEquipped == null || colorsEquipped.Length == 0)
        {
            LOG.Debug("Colors equipped are null or empty for source player");
            LOG.Debug($"colorsEquipped: {(colorsEquipped != null ? $"length {colorsEquipped.Length}" : "null")}");
            LOG.Warning("Cosmetic application cosmetics are empty");
            return;
        }

        var palette = MetaManager.instance != null ? MetaManager.instance.colors : null;
        if (palette == null || palette.Count == 0)
        {
            LOG.Debug("Palette are null or empty for source player");
            LOG.Debug($"palette: {(palette != null ? $"count {palette.Count}" : "null")}");
            LOG.Warning("Cosmetic application palette is empty");
            return;
        }

        var firstValidIndex = FindFirstValidColorIndex(colorsEquipped, palette.Count);
        if (firstValidIndex < 0)
        {
            LOG.Debug("No valid color index found in colors equipped for source player");
            LOG.Debug($"colorsEquipped: {string.Join(", ", colorsEquipped)}");
            LOG.Warning("Cosmetic application cosmetics are empty");
            return;
        }

        var cube = FindCube(droidRoot.transform);
        if (cube == null)
        {
            LOG.Warning("Cube transform not found for color application");
            return;
        }

        foreach (var renderer in cube.GetComponentsInChildren<Renderer>())
        {
            var lowerName = renderer.gameObject.name.ToLowerInvariant();
            if (IsExcluded(lowerName)) continue;

            var partType = ResolvePartType(lowerName);
            var colorIndex = ResolveColorIndex(colorsEquipped, palette.Count, partType, firstValidIndex);
            var color = palette[colorIndex].color;

            foreach (var material in renderer.materials)
            {
                material.SetColor(AlbedoColorKey, color);
            }
        }
    }

    private static bool IsExcluded(string lowerName)
    {
        return lowerName.Contains("eye") || lowerName.Contains("pupil") || lowerName.Contains("mesh_health");
    }

    private static Transform? FindCube(Transform root)
    {
        var cube = root.Find("Rigidbody/Cube") ?? root.Find("Cube");
        if (cube != null) return cube;

        foreach (var child in root.GetComponentsInChildren<Transform>())
        {
            if (child.name == "Cube") return child;
        }

        LOG.Warning("Cube transform not found for cube application");
        return null;
    }

    private static int FindFirstValidColorIndex(int[] colorsEquipped, int paletteCount)
    {
        foreach (var colorEquipped in colorsEquipped)
        {
            if (colorEquipped >= 0 && colorEquipped < paletteCount)
            {
                return colorEquipped;
            }
        }

        return -1;
    }

    private static SemiFunc.CosmeticType ResolvePartType(string lowerName)
    {
        if (lowerName.Contains("head")) return SemiFunc.CosmeticType.HeadTopMesh;
        if (lowerName.Contains("arm"))
        {
            return lowerName.Contains("left")
                ? SemiFunc.CosmeticType.ArmLeftMesh
                : SemiFunc.CosmeticType.ArmRightMesh;
        }

        if (lowerName.Contains("leg") || lowerName.Contains("foot"))
        {
            return lowerName.Contains("left")
                ? SemiFunc.CosmeticType.LegLeftMesh
                : SemiFunc.CosmeticType.LegRightMesh;
        }

        return SemiFunc.CosmeticType.BodyTopMesh;
    }

    private static int ResolveColorIndex(int[] colorsEquipped, int paletteCount, SemiFunc.CosmeticType part,
        int firstValidIndex)
    {
        var idx = (int)part;
        if (idx >= 0 && idx < colorsEquipped.Length)
        {
            var equipped = colorsEquipped[idx];
            if (equipped >= 0 && equipped < paletteCount) return equipped;
        }

        var bodyIdx = (int)SemiFunc.CosmeticType.BodyTopMesh;
        if (bodyIdx >= 0 && bodyIdx < colorsEquipped.Length)
        {
            var bodyEquipped = colorsEquipped[bodyIdx];
            if (bodyEquipped >= 0 && bodyEquipped < paletteCount) return bodyEquipped;
        }

        return firstValidIndex;
    }
}