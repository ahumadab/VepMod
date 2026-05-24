using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral.Visuals;

/// <summary>
///     Greffe le [RIG] du joueur source dans le Cube existant du droid (MyDroid/Enable/Cube).
///     Le Cube est conservé tel quel (Animator, BloodDust, Hurt Collider) : on lui retire
///     uniquement les composants joueur qui font NRE (PlayerAvatarVisuals, PlayerAvatarRightArm,
///     etc.) et on remplace son [RIG] enfant par celui du joueur source (qui amène ses bones,
///     ses cosmetic parents et ses cosmetic items déjà instanciés).
/// </summary>
internal static class DroidVisualsClone
{
    private const string RigName = "[RIG]";

    private static readonly VepLogger LOG = VepLogger.Create(nameof(DroidVisualsClone), true);

    private static readonly HashSet<string> KeepTypeNames = new()
    {
        "PlayerMaterial",
        "PlayerSpringImpulse"
    };

    // Composants Cosmetic* qu'on doit IMPÉRATIVEMENT détruire (au lieu de garder via la
    // règle "tout ce qui commence par Cosmetic"). Ils accèdent à des champs internes
    // non-sérialisés (playerCosmetics, cosmeticAsset, cosmeticTypeAsset) qui sont null
    // sur le clone → NRE en boucle dans leurs Update.
    private static readonly HashSet<string> DestroyCosmeticTypeNames = new()
    {
        "Cosmetic",
        "CosmeticBlocked"
    };

    /// <summary>
    ///     Localise MyDroid/Enable/Cube (= hôte de l'Animator et du [RIG]).
    /// </summary>
    public static Transform? FindCube(Transform droidRoot)
    {
        var cube = droidRoot.Find("Enable/Cube") ?? droidRoot.Find("Cube");
        if (cube != null) return cube;

        foreach (var child in droidRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == "Cube") return child;
        }

        LOG.Warning("FindCube: Cube transform not found under droid root");
        return null;
    }

    /// <summary>
    ///     Détruit les composants directement attachés à Cube qui dépendent du PlayerAvatar
    ///     source (NRE en cascade). Ne touche PAS les composants des enfants — ça sera fait
    ///     sur le sous-arbre [RIG] cloné, après instantiation.
    /// </summary>
    public static void SanitizeCube(Transform cube)
    {
        if (cube == null) return;

        foreach (var comp in cube.GetComponents<Component>())
        {
            if (comp == null) continue;
            if (ShouldKeep(comp)) continue;
            Object.DestroyImmediate(comp);
        }
    }

    /// <summary>
    ///     Détruit l'ancien [RIG] que le user avait copié dans son prefab — on va le
    ///     remplacer par un clone live du joueur source pour avoir ses cosmetics.
    /// </summary>
    public static void StripExistingRig(Transform cube)
    {
        if (cube == null) return;
        var rig = cube.Find(RigName);
        if (rig != null) Object.DestroyImmediate(rig.gameObject);
    }

    /// <summary>
    ///     Clone le [RIG] du joueur source sous Cube, à la place de l'ancien.
    /// </summary>
    public static bool TryCloneRig(
        PlayerAvatar sourcePlayer,
        Transform cube,
        out GameObject? cloneRig)
    {
        cloneRig = null;

        if (sourcePlayer == null || cube == null)
        {
            LOG.Warning("TryCloneRig: sourcePlayer or cube is null");
            return false;
        }

        var visuals = sourcePlayer.playerAvatarVisuals;
        if (visuals == null)
        {
            LOG.Warning($"TryCloneRig: sourcePlayer {sourcePlayer.playerName} has no playerAvatarVisuals");
            return false;
        }

        var sourceRig = visuals.transform.Find(RigName);
        if (sourceRig == null)
        {
            LOG.Warning($"TryCloneRig: source [RIG] not found under {sourcePlayer.playerName}.playerAvatarVisuals");
            return false;
        }

        var clone = Object.Instantiate(sourceRig.gameObject, cube);
        clone.name = RigName; // matcher les paths des animation clips
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        clone.SetActive(true);

        // Capture (spring → type) avant que Sanitize ne détruise les Cosmetic MonoBehaviours.
        var springTypes = CaptureSpringTypes(clone);
        SpringTypeCache = springTypes;

        Sanitize(clone);
        ApplyColors(clone, sourcePlayer);
        NormalizeRendering(clone);

        cloneRig = clone;

        var rendererCount = clone.GetComponentsInChildren<Renderer>(true).Length;
        LOG.Info($"Cloned [RIG] from {sourcePlayer.playerName}: cubePos={cube.position}, " +
                 $"rigLocalPos={clone.transform.localPosition}, " +
                 $"cubeLossy={cube.lossyScale}, " +
                 $"rigLossy={clone.transform.lossyScale}, " +
                 $"renderers={rendererCount}, isLocal={sourcePlayer.isLocal}");

        return true;
    }

    // Stocké entre TryCloneRig et AttachRelay (même thread, même frame).
    private static Dictionary<CosmeticSprings, SemiFunc.CosmeticType>? SpringTypeCache;

    // Anchor pré-existant dans le [RIG] sous Player Spring Impulse - Arm Left (gauche)
    // avec localPosition (0, -0.04, 0.471) et rotation quasi-identité. Le pendant
    // 'FollowTransformClient' du FlashlightController y pointe en vanilla pour le
    // rendu non-FPV. PlayerSpringImpulse est dans KeepTypeNames donc l'anchor
    // survit au Sanitize du rig cloné.
    private const string FlashlightAnchorName = "Flashlight Target Client";

    /// <summary>
    ///     Clone la Flashlight du joueur source et l'attache à l'anchor 'Flashlight
    ///     Target Client' (main gauche) du rig cloné. Le FlashlightController est
    ///     strippé avant que son Start ne tourne (sinon il reparenterait vers la
    ///     Flashlight Target FPV source, hors du droid). Mesh + spotlight + halo
    ///     sont forcés en ON et la layer bascule sur "PlayerVisuals" (vs "Triggers"
    ///     qui rend la lampe non-locale invisible en vanilla).
    /// </summary>
    public static bool TryCloneFlashlight(PlayerAvatar sourcePlayer, GameObject cloneRig)
    {
        if (sourcePlayer == null || cloneRig == null) return false;

        var sourceController = sourcePlayer.flashlightController
                               ?? sourcePlayer.GetComponentInChildren<FlashlightController>(true);
        if (sourceController == null)
        {
            LOG.Warning($"TryCloneFlashlight: no FlashlightController on {sourcePlayer.playerName}");
            return false;
        }

        var anchor = DroidHelpers.FindChildByName(cloneRig.transform, FlashlightAnchorName);
        if (anchor == null)
        {
            LOG.Warning($"TryCloneFlashlight: anchor '{FlashlightAnchorName}' not found in clone rig " +
                        "(Player Spring Impulse - Arm Left peut-être strippé par Sanitize)");
            return false;
        }

        var sourceFlashlight = sourceController.gameObject;
        var clone = Object.Instantiate(sourceFlashlight, anchor);
        clone.name = "Flashlight";
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;

        // Capture les refs visuelles AVANT de strip le controller.
        var clonedController = clone.GetComponent<FlashlightController>();
        var mesh = clonedController != null ? clonedController.mesh : null;
        var meshShadows = clonedController != null ? clonedController.meshShadows : null;
        var spotlight = clonedController != null ? clonedController.spotlight : null;
        var halo = clonedController != null ? clonedController.halo : null;

        // Strip tous les comportements joueur AVANT que Start ne tourne (Start
        // reparenterait vers FollowTransformLocal source, sortant la lampe du droid).
        foreach (var comp in clone.GetComponentsInChildren<Component>(true))
        {
            if (comp == null) continue;
            if (comp is Transform) continue;
            if (comp is MeshFilter) continue;
            if (comp is MeshRenderer) continue;
            if (comp is Light) continue;
            Object.DestroyImmediate(comp);
        }

        if (mesh != null) mesh.enabled = true;
        if (meshShadows != null) meshShadows.enabled = true;
        if (spotlight != null) spotlight.enabled = true;
        if (halo != null) halo.enabled = true;

        var visualsLayer = LayerMask.NameToLayer("PlayerVisuals");
        if (visualsLayer < 0) visualsLayer = 0;
        foreach (var t in clone.GetComponentsInChildren<Transform>(true))
        {
            if (t != null) t.gameObject.layer = visualsLayer;
        }

        clone.SetActive(true);
        LOG.Info($"Flashlight cloned under '{FlashlightAnchorName}'");
        return true;
    }

    public static void AttachRelay(Animator animator, DroidController droid, GameObject cloneRig)
    {
        if (animator == null) return;
        var relay = animator.gameObject.AddComponent<DroidAvatarVisualsRelay>();
        relay.Initialize(droid, cloneRig, SpringTypeCache ?? new Dictionary<CosmeticSprings, SemiFunc.CosmeticType>());
        SpringTypeCache = null;
    }

    private static Dictionary<CosmeticSprings, SemiFunc.CosmeticType> CaptureSpringTypes(GameObject clone)
    {
        var map = new Dictionary<CosmeticSprings, SemiFunc.CosmeticType>();
        foreach (var cosmetic in clone.GetComponentsInChildren<Cosmetic>(true))
        {
            if (cosmetic == null) continue;
            var type = cosmetic.type;
            foreach (var spring in cosmetic.GetComponentsInChildren<CosmeticSprings>(true))
            {
                if (spring != null) map[spring] = type;
            }
        }
        return map;
    }

    /// <summary>
    ///     Whitelist : on conserve uniquement les composants utiles au rendu et à
    ///     l'animation cosmétique. Tout le reste (PlayerAvatar*, PlayerExpression,
    ///     PlayerCosmetics, Photon*, AudioSource voix, etc.) est détruit pour éviter
    ///     les NRE en cascade dans leurs Update.
    /// </summary>
    private static void Sanitize(GameObject clone)
    {
        foreach (var comp in clone.GetComponentsInChildren<Component>(true))
        {
            if (comp == null) continue;
            if (ShouldKeep(comp)) continue;
            Object.DestroyImmediate(comp);
        }
    }

    private static bool ShouldKeep(Component comp)
    {
        if (comp is Transform) return true;
        if (comp is Animator) return true;
        if (comp is Renderer) return true;
        if (comp is MeshFilter) return true;
        if (comp is ParticleSystem) return true;
        if (comp is Light) return true;
        if (comp is IConstraint) return true;

        var typeName = comp.GetType().Name;

        // Exclusions explicites : Cosmetic et CosmeticBlocked NRE en boucle (cf.
        // DestroyCosmeticTypeNames). On capture leur état utile avant via
        // CaptureSpringTypes pour ne pas perdre le mapping spring → type.
        if (DestroyCosmeticTypeNames.Contains(typeName)) return false;

        // Tout le reste Cosmetic* est utile : CosmeticSprings (anim cosmétique),
        // CosmeticCustomSpring, CosmeticHideCondition, CosmeticOffsetCondition,
        // CosmeticSwitchCondition, CosmeticCustomCondition, CosmeticLookDown, etc.
        if (typeName.StartsWith("Cosmetic")) return true;

        if (KeepTypeNames.Contains(typeName)) return true;

        return false;
    }

    private static void ApplyColors(GameObject clone, PlayerAvatar sourcePlayer)
    {
        var cosmetics = sourcePlayer.playerCosmetics;
        if (cosmetics == null || cosmetics.colorsEquipped == null) return;
        if (MetaManager.instance == null || MetaManager.instance.colors == null) return;

        var paletteCount = MetaManager.instance.colors.Count;
        if (paletteCount == 0) return;

        var albedoColor = Shader.PropertyToID("_AlbedoColor");
        var emissionColor = Shader.PropertyToID("_EmissionColor");
        var fresnelColor = Shader.PropertyToID("_FresnelColor");

        var sourceColors = cosmetics.colorsEquipped;

        foreach (var playerMaterial in clone.GetComponentsInChildren<PlayerMaterial>(true))
        {
            if (playerMaterial == null) continue;

            playerMaterial.Setup();
            if (playerMaterial.material == null) continue;

            var typeIndex = (int)playerMaterial.cosmeticType;
            if (typeIndex < 0 || typeIndex >= sourceColors.Length) continue;

            var colorIndex = sourceColors[typeIndex];
            if (colorIndex < 0 || colorIndex >= paletteCount) continue;

            playerMaterial.ColorSet(albedoColor, emissionColor, fresnelColor, colorIndex);
        }
    }

    /// <summary>
    ///     Force tous les renderers à projeter normalement (le local player se masque
    ///     en ShadowsOnly et utilise la layer "PlayerVisualsLocal" masquée côté caméra).
    ///     On bascule sur "PlayerVisuals" (layer des avatars non-locaux, visible).
    /// </summary>
    private static void NormalizeRendering(GameObject clone)
    {
        var visualsLayer = LayerMask.NameToLayer("PlayerVisuals");
        if (visualsLayer < 0) visualsLayer = 0;

        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.enabled = true;
            renderer.gameObject.layer = visualsLayer;
        }

        foreach (var smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null) continue;
            smr.updateWhenOffscreen = true;
        }
    }
}
