using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral.Visuals;

/// <summary>
///     Grafts the cloned player's [RIG] into the Hallucination prefab's Cube (MyDroid/Enable/Cube). The Cube keeps
///     its Animator, BloodDust and Hurt Collider; only the player components that throw (PlayerAvatarVisuals,
///     PlayerAvatarRightArm…) are removed, and its [RIG] child is replaced by the cloned player's, which brings
///     the bones, cosmetic parents and already instantiated cosmetic items.
/// </summary>
internal static class DroidVisualsClone
{
    private const string RigName = "[RIG]";

    // An anchor already in the [RIG], under Player Spring Impulse - Arm Left, at localPosition (0, -0.04, 0.471)
    // with a near-identity rotation. In the game, the FlashlightController's 'FollowTransformClient' points there
    // for third-person rendering. PlayerSpringImpulse is in KeepTypeNames, so the anchor survives Sanitize.
    private const string FlashlightAnchorName = "Flashlight Target Client";
    private const string MapToolAnchorName = "Map Tool Target Client";

    private static readonly VepLogger LOG = VepLogger.Create(nameof(DroidVisualsClone), true);

    private static readonly HashSet<string> KeepTypeNames = new()
    {
        "PlayerMaterial",
        "PlayerSpringImpulse"
    };

    // Exceptions to keeping every Cosmetic* component: these read non-serialized fields (playerCosmetics,
    // cosmeticAsset, cosmeticTypeAsset) that are null on the clone, and throw in their Update every frame.
    private static readonly HashSet<string> DestroyCosmeticTypeNames = new()
    {
        "Cosmetic",
        "CosmeticBlocked"
    };

    // Held from TryCloneRig to AttachRelay, in the same frame.
    private static Dictionary<CosmeticSprings, SemiFunc.CosmeticType>? SpringTypeCache;

    /// <summary>
    ///     MyDroid/Enable/Cube, which holds the Animator and the [RIG].
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
    ///     Cube's own components that need the cloned player's PlayerAvatar, and throw without it. Its children are
    ///     handled on the cloned [RIG] after instantiation.
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
    ///     The prefab's own [RIG] makes way for a live clone, which carries the player's cosmetics.
    /// </summary>
    public static void StripExistingRig(Transform cube)
    {
        if (cube == null) return;
        var rig = cube.Find(RigName);
        if (rig != null) Object.DestroyImmediate(rig.gameObject);
    }

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
        clone.name = RigName; // the animation paths name it
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        clone.SetActive(true);

        // Before Sanitize destroys the Cosmetic MonoBehaviours that know each spring's type.
        var springTypes = CaptureSpringTypes(clone);
        SpringTypeCache = springTypes;

        Sanitize(clone);
        ApplyColors(clone, sourcePlayer);
        NormalizeRendering(clone);
        ForceStandingPose(clone);

        cloneRig = clone;

        var rendererCount = clone.GetComponentsInChildren<Renderer>(true).Length;
        LOG.Debug($"Cloned [RIG] from {sourcePlayer.playerName}: cubePos={cube.position}, " +
                  $"rigLocalPos={clone.transform.localPosition}, " +
                  $"cubeLossy={cube.lossyScale}, " +
                  $"rigLossy={clone.transform.lossyScale}, " +
                  $"renderers={rendererCount}, isLocal={sourcePlayer.isLocal}");

        return true;
    }

    /// <summary>
    ///     Attaches the cloned player's flashlight to the left-hand 'Flashlight Target Client' anchor of the cloned rig.
    ///     The FlashlightController is stripped before its Start runs, which would reparent the flashlight to the
    ///     player's first-person target, outside the Hallucination. Mesh, spotlight and halo are forced on, and the
    ///     layer becomes "PlayerVisuals": on "Triggers" the game keeps a non-local flashlight invisible.
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

        // The controller holds the visual references, so they are read before it is stripped.
        var clonedController = clone.GetComponent<FlashlightController>();
        var mesh = clonedController != null ? clonedController.mesh : null;
        var meshShadows = clonedController != null ? clonedController.meshShadows : null;
        var spotlight = clonedController != null ? clonedController.spotlight : null;
        var halo = clonedController != null ? clonedController.halo : null;

        // Before Start runs: it would reparent the flashlight to the player's FollowTransformLocal.
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

        ResetFlashlightInternals(clone);

        var visualsLayer = LayerMask.NameToLayer("PlayerVisuals");
        if (visualsLayer < 0) visualsLayer = 0;
        foreach (var t in clone.GetComponentsInChildren<Transform>(true))
        {
            if (t != null) t.gameObject.layer = visualsLayer;
        }

        clone.SetActive(true);
        LOG.Debug($"Flashlight cloned under '{FlashlightAnchorName}'");
        return true;
    }

    // Flashlight transforms that stripped scripts drive at runtime: ToolBackAway on 'Back Away', the sprint logic on
    // 'Flashlight Sprint', FlashlightController on 'Flashlight Hide' and 'Flashlight Click'. Cloning a player whose
    // flashlight is not in its neutral pose (crouching rotates 'Flashlight Hide') bakes a wrong orientation, so they
    // go back to their prefab defaults.
    private static readonly (string Name, Vector3 Pos)[] FlashlightNeutralNodes =
    {
        ("Back Away Offset", new Vector3(0f, 0f, -0.044f)),
        ("Back Away", new Vector3(0f, 0f, 0.044f)),
        ("Flashlight Sprint", Vector3.zero),
        ("Flashlight Hide", Vector3.zero),
        ("Flashlight Click", Vector3.zero)
    };

    private static void ResetFlashlightInternals(GameObject clone)
    {
        foreach (var (name, pos) in FlashlightNeutralNodes)
        {
            var node = DroidHelpers.FindChildByName(clone.transform, name);
            if (node == null) continue;
            node.localPosition = pos;
            node.localRotation = Quaternion.identity;
            node.localScale = Vector3.one;
        }
    }

    /// <summary>
    ///     Attaches the cloned player's map tool, visuals only, to the right-hand 'Map Tool Target Client' anchor
    ///     (under Player Spring Impulse - Arm Right) of the cloned rig. Returned inactive: the caller activates it
    ///     when the Hallucination enters CheckMap.
    /// </summary>
    public static bool TryCloneMapTool(PlayerAvatar sourcePlayer, GameObject cloneRig, out GameObject? clonedMapTool)
    {
        clonedMapTool = null;
        if (sourcePlayer == null || cloneRig == null) return false;

        // PlayerAvatar sits on 'Player Avatar Controller', a child of the prefab root, and 'Map Tool' is its sibling:
        // GetComponentInChildren cannot reach it, the search starts from transform.root.
        var sourceMapToolTransform = sourcePlayer.transform.root.Find("Map Tool");
        if (sourceMapToolTransform == null)
        {
            LOG.Warning($"TryCloneMapTool: 'Map Tool' not found under root of {sourcePlayer.playerName}");
            return false;
        }

        var anchor = DroidHelpers.FindChildByName(cloneRig.transform, MapToolAnchorName);
        if (anchor == null)
        {
            LOG.Warning($"TryCloneMapTool: anchor '{MapToolAnchorName}' not found in clone rig " +
                        "(Player Spring Impulse - Arm Right peut-être strippé par Sanitize)");
            return false;
        }

        var sourceMapTool = sourceMapToolTransform.gameObject;
        var clone = Object.Instantiate(sourceMapTool, anchor);
        clone.name = "Map Tool";
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;

        // Before their Start runs, which reaches for the player's PlayerAvatar and photonView.
        foreach (var comp in clone.GetComponentsInChildren<Component>(true))
        {
            if (comp == null) continue;
            if (comp is Transform) continue;
            if (comp is MeshFilter) continue;
            if (comp is MeshRenderer) continue;
            if (comp is SkinnedMeshRenderer) continue;
            if (comp is Light) continue;
            Object.DestroyImmediate(comp);
        }

        // MapToolController hides them by default and animates them through HideLerp.
        foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
        {
            if (r != null) r.enabled = true;
        }

        // MapToolController.Start deactivates VisualTransform on the player, and the clone inherits it. Active, it
        // follows the root that ShowMapTool toggles.
        var visuals = DroidHelpers.FindChildByName(clone.transform, "Visuals");
        if (visuals != null) visuals.gameObject.SetActive(true);

        // The controller turns the 'Hide' pivot 90° on X when hidden (HideLerp = 1). With no controller on the clone,
        // identity is the held-out pose.
        var hide = DroidHelpers.FindChildByName(clone.transform, "Hide");
        if (hide != null)
        {
            hide.localRotation = Quaternion.identity;
            hide.localScale = Vector3.one;
        }

        var visualsLayer = LayerMask.NameToLayer("PlayerVisuals");
        if (visualsLayer < 0) visualsLayer = 0;
        foreach (var t in clone.GetComponentsInChildren<Transform>(true))
        {
            if (t != null) t.gameObject.layer = visualsLayer;
        }

        var rendererCount = clone.GetComponentsInChildren<Renderer>(true).Length;
        LOG.Debug($"MapTool cloned under '{MapToolAnchorName}' " +
                  $"(renderers={rendererCount}, visualsFound={visuals != null}, hideFound={hide != null})");

        clone.SetActive(false);
        clonedMapTool = clone;
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
    ///     Keeps only what rendering and cosmetic animation need. Everything else (PlayerAvatar*, PlayerExpression,
    ///     PlayerCosmetics, Photon*, the voice AudioSource…) is destroyed: it would throw in its Update.
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

        // See DestroyCosmeticTypeNames; CaptureSpringTypes keeps what they knew.
        if (DestroyCosmeticTypeNames.Contains(typeName)) return false;

        // The other Cosmetic* components animate the cosmetics: CosmeticSprings, CosmeticCustomSpring,
        // CosmeticHideCondition, CosmeticOffsetCondition, CosmeticSwitchCondition, CosmeticLookDown…
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

    // Every bone whose position, rotation or scale the Crouch or Crawl animations override; each has an identity
    // prefab default. The whole transform is reset, not just the deformed property: the game's animator has
    // Write Defaults on, so every state puts the bones it does not animate back to the values captured at Rebind.
    // Stand Move does not animate ANIM BODY TOP, for instance: if the captured default were the crouched pose
    // baked at clone time, the upper body and head would sag while walking only. Reset before Rebind, the captured
    // defaults are standing.
    private static readonly HashSet<string> IdentityResetBones = new()
    {
        "ANIM ARM L",
        "ANIM ARM R",
        "ANIM ARM R SCALE",
        "ANIM BODY BOT",
        "ANIM BODY TOP",
        "ANIM BOT",
        "ANIM HEAD BOT",
        "ANIM LEG L BOT",
        "ANIM LEG L TOP",
        "ANIM LEG R BOT",
        "ANIM LEG R TOP",
        "Cosmetic Parent - Head Bottom Mesh",
        "Player Spring Impulse - Body Bottom",
        "Player Spring Impulse - Body Top",
        "code_arm_l"
    };

    // Parents the Crouch and Crawl animations switch on and off as a block; standing, they are on.
    private static readonly HashSet<string> ActivateParents = new()
    {
        "Cosmetic Parent - Body Bottom Mesh",
        "Cosmetic Parent - Body Top Mesh"
    };

    // Crouch and Crawl deform the map tool anchor too, but its standing values are prefab offsets, not identity.
    // Without them, the cloned map tool under it would inherit a crouched rotation and scale.
    private const string MapToolAnchorBone = "Map Tool Target Client";
    private static readonly Vector3 MapToolAnchorPos = new(0.007999987f, -0.084999986f, 0.4980001f);
    private static readonly Quaternion MapToolAnchorRot = new(-0.58078414f, -0.22498398f, 0.48120198f, 0.6168604f);

    /// <summary>
    ///     Puts the cloned rig in its standing pose, whatever the player was doing when cloned. Must run before
    ///     Animator.Rebind(): see IdentityResetBones for why.
    ///
    ///     On what Crouch and Crawl deform (found by diffing the animation curves):
    ///     - active states: 'Mesh Sphere' on, 'Mesh Flat' off, and the 'Cosmetic Parent - Body Bottom/Top Mesh'
    ///       parents on;
    ///     - the whole transform of each Crouch or Crawl bone back to identity;
    ///     - the map tool anchor back to its prefab offsets.
    ///     A no-op when the player was already standing.
    /// </summary>
    private static void ForceStandingPose(GameObject clone)
    {
        foreach (var t in clone.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;

            switch (t.name)
            {
                case "Mesh Sphere":
                    t.gameObject.SetActive(true);
                    continue;
                case "Mesh Flat":
                    t.gameObject.SetActive(false);
                    continue;
            }

            if (ActivateParents.Contains(t.name))
            {
                t.gameObject.SetActive(true);
            }
            else if (t.name == MapToolAnchorBone)
            {
                t.localPosition = MapToolAnchorPos;
                t.localRotation = MapToolAnchorRot;
                t.localScale = Vector3.one;
            }
            else if (IdentityResetBones.Contains(t.name))
            {
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
            }
        }
    }

    /// <summary>
    ///     The local player's renderers are ShadowsOnly on the camera-hidden "PlayerVisualsLocal" layer; a clone of the
    ///     local player is moved to "PlayerVisuals", the visible layer of non-local avatars, with normal shadows.
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
