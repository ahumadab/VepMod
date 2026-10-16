using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Vanish and Hallucinations of one Victim, on the Victim's client: one Imitation per Imitated Player, from
///     <see cref="Begin" /> to <see cref="End" />.
/// </summary>
public sealed class Imitations : MonoBehaviourPunCallbacks
{
    private const float SpawnRetryDelay = 1f; // s

    private static readonly VepLogger LOG = VepLogger.Create<Imitations>();

    private static readonly Dictionary<Type, FieldInfo[]> SoundEmitterFields = new();
    private static readonly List<Renderer> RendererBuffer = new();
    private static readonly List<Light> LightBuffer = new();
    private static readonly List<AudioSource> AudioSourceBuffer = new();
    private static readonly List<Hallucination> SpeakerBuffer = new();

    private readonly Dictionary<PlayerAvatar, Imitation> _imitations = new();
    private Coroutine? _spawning;
    private PlayerAvatar _victim = null!;

    /// <summary>The local player's: null while they are not a Victim.</summary>
    public static Imitations? Local => PlayerAvatar.instance ? PlayerAvatar.instance.GetComponent<Imitations>() : null;

    private void Awake()
    {
        _victim = GetComponent<PlayerAvatar>();
    }

    // The game turns an Imitated Player's flashlight and map tool back on; they are hidden again after it.
    private void LateUpdate()
    {
        foreach (var (imitatedPlayer, imitation) in _imitations)
        {
            if (!imitatedPlayer) continue;

            var flashlight = imitatedPlayer.flashlightController;
            if (flashlight && (flashlight.mesh.enabled || flashlight.meshShadows.enabled ||
                               flashlight.spotlight.enabled || (flashlight.halo && flashlight.halo.enabled)))
            {
                SetFlashlightVisible(imitatedPlayer, false);
            }

            var mapTool = imitation.MapTool;
            if (mapTool && mapTool!.VisualTransform && mapTool.VisualTransform.gameObject.activeSelf)
            {
                mapTool.VisualTransform.gameObject.SetActive(false);
            }

            // The game instantiates parts at runtime (grab grid, TTS voice): they are hidden in the frame they appear.
            HideNewParts(imitatedPlayer, imitation);
        }
    }

    private void OnDestroy()
    {
        End();
    }

    public void Begin()
    {
        // A MapToolController is not under its PlayerAvatar in the hierarchy: it is found in the scene by its
        // PlayerAvatar property.
        var sceneMapTools = FindObjectsOfType<MapToolController>(true);

        foreach (var player in GameDirector.instance.PlayerList)
        {
            if (!player || player == _victim) continue;

            MapToolController? mapTool = null;
            foreach (var candidate in sceneMapTools)
            {
                if (candidate.PlayerAvatar == player) mapTool = candidate;
            }

            var imitation = new Imitation(player, mapTool);
            CollectSoundEmitters(imitation);
            _imitations[player] = imitation;
            SetVisible(player, imitation, false);
            LOG.Debug($"{player.playerName} is imitated for {_victim.playerName}");
        }

        _spawning = StartCoroutine(SpawnHallucinations(new List<PlayerAvatar>(_imitations.Keys)));
    }

    public void End()
    {
        if (_spawning != null)
        {
            StopCoroutine(_spawning);
            _spawning = null;
        }

        foreach (var (imitatedPlayer, imitation) in _imitations)
        {
            EndImitation(imitatedPlayer, imitation);
        }

        _imitations.Clear();
    }

    // Photon calls this before it destroys the leaving player's avatar, which is still in the PlayerList here.
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        var leavingPlayer = SemiFunc.PlayerAvatarGetFromPhotonPlayer(otherPlayer);
        if (!leavingPlayer || !_imitations.TryGetValue(leavingPlayer, out var imitation)) return;

        LOG.Debug($"{leavingPlayer.playerName} left the game");
        EndImitation(leavingPlayer, imitation);
        _imitations.Remove(leavingPlayer);
    }

    /// <returns>
    ///     A spawned Hallucination whose Imitated Player has a Voice Sample in the local Voice Bank, at random; null when
    ///     none has.
    /// </returns>
    public Hallucination? PickMimicSpeaker()
    {
        var spawned = 0;
        SpeakerBuffer.Clear();
        foreach (var (imitatedPlayer, imitation) in _imitations)
        {
            if (!imitatedPlayer || !imitation.Hallucination) continue;

            spawned++;
            if (VepMod.VoiceBanks.HasVoiceSample(imitatedPlayer.VoiceBankKey()))
            {
                SpeakerBuffer.Add(imitation.Hallucination!);
            }
        }

        if (SpeakerBuffer.Count == 0)
        {
            LOG.Debug(spawned == 0
                ? $"Mimic for {_victim.playerName}: no Hallucination spawned yet, nothing plays."
                : $"Mimic for {_victim.playerName}: none of the {spawned} Hallucinations has a Voice Sample, nothing plays.");
            return null;
        }

        var speaker = SpeakerBuffer[UnityEngine.Random.Range(0, SpeakerBuffer.Count)];
        LOG.Debug($"Mimic for {_victim.playerName}: {SpeakerBuffer.Count} of {spawned} Hallucinations have a Voice " +
                  $"Sample, picked Hallucination {speaker.GetInstanceID()} of {speaker.ImitatedPlayer.playerName}.");
        return speaker;
    }

    public bool IsImitatedPlayer(PlayerAvatar player)
    {
        return _imitations.ContainsKey(player);
    }

    /// <param name="emitter">
    ///     A <see cref="Sound" /> or a <see cref="Materials.MaterialTrigger" /> held by a component, or the Transform a
    ///     sound follows.
    /// </param>
    public bool IsImitatedPlayerSound(object emitter)
    {
        foreach (var (imitatedPlayer, imitation) in _imitations)
        {
            if (!imitatedPlayer) continue;

            string? emitterName = null;
            if (emitter is Transform followed)
            {
                if (followed && followed.IsChildOf(imitation.Root)) emitterName = followed.name;
            }
            else
            {
                imitation.SoundEmitters.TryGetValue(emitter, out emitterName);
            }

            if (emitterName == null) continue;

            LOG.Debug($"{emitterName} of {imitatedPlayer.playerName} muted for {_victim.playerName}");
            return true;
        }

        return false;
    }

    private void EndImitation(PlayerAvatar imitatedPlayer, Imitation imitation)
    {
        if (imitation.Hallucination)
        {
            Destroy(imitation.Hallucination!.gameObject);
        }

        if (imitatedPlayer)
        {
            SetVisible(imitatedPlayer, imitation, true);
            LOG.Debug($"{imitatedPlayer.playerName} is no longer imitated for {_victim.playerName}");
        }
    }

    // One Hallucination at a time, a frame after its spawn-point search: creating every Hallucination in the frame the
    // Victim became Haunted froze the game.
    private IEnumerator SpawnHallucinations(List<PlayerAvatar> imitatedPlayers)
    {
        foreach (var imitatedPlayer in imitatedPlayers)
        {
            while (imitatedPlayer && _imitations.TryGetValue(imitatedPlayer, out var imitation))
            {
                var spawnPosition = FindSpawnPosition();
                yield return null;

                // The Imitated Player may have left the game during that frame.
                if (!_imitations.ContainsKey(imitatedPlayer)) break;

                var hallucination = spawnPosition.HasValue
                    ? Hallucination.Create(imitatedPlayer, spawnPosition.Value)
                    : null;
                if (hallucination)
                {
                    imitation.Hallucination = hallucination;
                    var distance = Vector3.Distance(spawnPosition!.Value, _victim.transform.position);
                    LOG.Debug($"Hallucination of {imitatedPlayer.playerName} spawned {distance:F1} m from the Victim");
                    break;
                }

                LOG.Warning($"Hallucination of {imitatedPlayer.playerName} failed to spawn, retrying");
                yield return new WaitForSeconds(SpawnRetryDelay);
            }
        }

        _spawning = null;
    }

    private Vector3? FindSpawnPosition()
    {
        var victimPosition = _victim.transform.position;

        var levelPoint = SemiFunc.LevelPointGet(victimPosition, Hallucination.SpawnDistanceMin,
            Hallucination.SpawnDistanceMax);
        if (!levelPoint)
        {
            levelPoint = SemiFunc.LevelPointGet(victimPosition, 0f, 999f);
        }

        if (!levelPoint)
        {
            LOG.Warning("No LevelPoint found for a Hallucination spawn");
            return null;
        }

        return levelPoint.transform.position;
    }

    private static void SetVisible(PlayerAvatar player, Imitation imitation, bool visible)
    {
        if (player.playerAvatarVisuals && player.playerAvatarVisuals.meshParent)
        {
            player.playerAvatarVisuals.meshParent.SetActive(visible);
        }

        if (player.worldSpaceUIPlayerName)
        {
            player.worldSpaceUIPlayerName.gameObject.SetActive(visible);
        }

        SetFlashlightVisible(player, visible);

        var mapTool = imitation.MapTool;
        if (mapTool && mapTool!.VisualTransform)
        {
            mapTool.VisualTransform.gameObject.SetActive(visible);
        }

        if (visible)
        {
            RestoreParts(player, imitation);
        }
        else
        {
            HideNewParts(player, imitation);
        }
    }

    // Every component of the player's prefab, and of the death head and tumble it spawned, holds its own Sound and
    // MaterialTrigger instances; sounds of shared presets (impacts) are told apart by the Transform they follow.
    private static void CollectSoundEmitters(Imitation imitation)
    {
        foreach (var behaviour in imitation.Root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!behaviour) continue;

            foreach (var field in SoundEmitterFieldsOf(behaviour.GetType()))
            {
                var emitter = field.GetValue(behaviour);
                if (emitter != null) imitation.SoundEmitters[emitter] = $"{field.DeclaringType!.Name}.{field.Name}";
            }
        }
    }

    private static FieldInfo[] SoundEmitterFieldsOf(Type type)
    {
        if (SoundEmitterFields.TryGetValue(type, out var cached)) return cached;

        var fields = new List<FieldInfo>();
        for (var declaring = type; declaring != null && declaring != typeof(MonoBehaviour); declaring = declaring.BaseType)
        {
            foreach (var field in declaring.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                      BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(Sound) || field.FieldType == typeof(Materials.MaterialTrigger))
                {
                    fields.Add(field);
                }
            }
        }

        return SoundEmitterFields[type] = fields.ToArray();
    }

    // The game never sets forceRenderingOff (which drops shadows too), a light's culling mask or an AudioSource's
    // mute: they survive its own toggles (ApplyLocalVisibility, death effects, death head) and give back exactly the
    // state it left.
    private static void HideNewParts(PlayerAvatar player, Imitation imitation)
    {
        if (!imitation.Root) return;

        var renderers = 0;
        imitation.Root.GetComponentsInChildren(true, RendererBuffer);
        foreach (var renderer in RendererBuffer)
        {
            if (renderer.forceRenderingOff || imitation.IsCopiedByHallucination(renderer.transform)) continue;

            renderer.forceRenderingOff = true;
            imitation.HiddenRenderers.Add(renderer);
            renderers++;
        }

        var lights = 0;
        imitation.Root.GetComponentsInChildren(true, LightBuffer);
        foreach (var light in LightBuffer)
        {
            if (light.cullingMask == 0 || imitation.IsCopiedByHallucination(light.transform)) continue;

            imitation.HiddenLights.Add((light, light.cullingMask));
            light.cullingMask = 0;
            lights++;
        }

        var sources = MuteNewSources(imitation, imitation.Root);
        if (player.voiceChat)
        {
            sources += MuteNewSources(imitation, player.voiceChat.transform);
        }

        if (renderers + lights + sources > 0)
        {
            LOG.Debug($"{player.playerName}: {renderers} renderers and {lights} lights hidden, {sources} audio " +
                      "sources muted");
        }
    }

    private static int MuteNewSources(Imitation imitation, Transform root)
    {
        var muted = 0;
        root.GetComponentsInChildren(true, AudioSourceBuffer);
        foreach (var source in AudioSourceBuffer)
        {
            if (source.mute) continue;

            source.mute = true;
            imitation.MutedSources.Add(source);
            muted++;
        }

        return muted;
    }

    private static void RestoreParts(PlayerAvatar player, Imitation imitation)
    {
        foreach (var renderer in imitation.HiddenRenderers)
        {
            if (renderer) renderer.forceRenderingOff = false;
        }

        foreach (var (light, cullingMask) in imitation.HiddenLights)
        {
            if (light) light.cullingMask = cullingMask;
        }

        foreach (var source in imitation.MutedSources)
        {
            if (source) source.mute = false;
        }

        LOG.Debug($"{player.playerName}: {imitation.HiddenRenderers.Count} renderers, {imitation.HiddenLights.Count} " +
                  $"lights and {imitation.MutedSources.Count} audio sources restored");
        imitation.HiddenRenderers.Clear();
        imitation.HiddenLights.Clear();
        imitation.MutedSources.Clear();
    }

    private static void SetFlashlightVisible(PlayerAvatar player, bool visible)
    {
        var flashlight = player.flashlightController;
        if (!flashlight) return;

        // FlashlightController only toggles them when its state changes: shown again, they take the values its current
        // state gave them, or a crouching player's flashlight would stay lit.
        var meshShown = visible && flashlight.currentState != FlashlightController.State.Hidden;
        var lightShown = visible && flashlight.LightActive;
        flashlight.mesh.enabled = meshShown;
        flashlight.meshShadows.enabled = meshShown;
        flashlight.spotlight.enabled = lightShown;
        if (flashlight.halo)
        {
            flashlight.halo.enabled = lightShown;
        }
    }

    private sealed class Imitation
    {
        public readonly List<(Light Light, int CullingMask)> HiddenLights = new();
        public readonly List<Renderer> HiddenRenderers = new();
        public readonly MapToolController? MapTool;
        public readonly List<AudioSource> MutedSources = new();

        // PlayerAvatar sits on a child of the prefab root, beside its visuals, effects and grab beam; the death head
        // and the tumble move under that root once set up.
        public readonly Transform Root;

        // Each emitter named after the field that holds it.
        public readonly Dictionary<object, string> SoundEmitters = new();

        private readonly Transform? _flashlight;
        private readonly Transform? _rig;

        // Null until it has spawned.
        public Hallucination? Hallucination;

        public Imitation(PlayerAvatar player, MapToolController? mapTool)
        {
            MapTool = mapTool;
            Root = player.transform.parent ? player.transform.parent : player.transform;
            _rig = player.playerAvatarVisuals && player.playerAvatarVisuals.meshParent
                ? player.playerAvatarVisuals.meshParent.transform
                : null;
            _flashlight = player.flashlightController ? player.flashlightController.transform : null;
        }

        // A Hallucination copies the rig, flashlight and map tool, light masks included: Vanish deactivates or
        // disables those instead.
        public bool IsCopiedByHallucination(Transform part)
        {
            return (_rig && part.IsChildOf(_rig)) || (_flashlight && part.IsChildOf(_flashlight)) ||
                   (MapTool && part.IsChildOf(MapTool!.transform.parent));
        }
    }
}
