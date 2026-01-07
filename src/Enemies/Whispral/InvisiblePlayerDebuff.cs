using System.Collections.Generic;
using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

public sealed class InvisiblePlayerDebuff : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<InvisiblePlayerDebuff>();
    private static readonly HashSet<PlayerAvatar> GlobalHiddenPlayers = new();

    private readonly List<PlayerAvatar> _hiddenPlayers = new();
    private readonly Dictionary<PlayerAvatar, MapToolController> _mapToolCache = new();
    public bool IsActive { get; private set; }

    /// <summary>
    ///     Liste des joueurs actuellement cachés par ce debuff.
    ///     Utilisé par HallucinationDebuff pour créer les hallucinations.
    /// </summary>
    public IReadOnlyList<PlayerAvatar> HiddenPlayers => _hiddenPlayers;

    private void LateUpdate()
    {
        LateCheckDeactivateVisuals();
    }

    private void OnDestroy()
    {
        if (!IsActive || _hiddenPlayers.Count == 0) return;

        LOG.Debug("InvisiblePlayerDebuff destroyed while active, restoring visibility.");
        foreach (var player in _hiddenPlayers)
        {
            if (!player) continue;
            GlobalHiddenPlayers.Remove(player);
            SetPlayerVisibility(player, true);
        }
        _hiddenPlayers.Clear();
        _mapToolCache.Clear();
        IsActive = false;
    }

    public void ApplyDebuff(bool invisible)
    {
        IsActive = invisible;
        if (!SemiFunc.IsMultiplayer())
        {
            LOG.Debug("Singleplayer mode detected, skipping invisibility application.");
            return;
        }

        LOG.Debug($"Applying invisibility debuff: {invisible}");
        var instancePlayerList = GameDirector.instance.PlayerList;
        foreach (var player in instancePlayerList)
        {
            if (CheckNull(player)) continue;

            // Skip le joueur local (celui qui subit le debuff)
            if (player.photonView.IsMine)
            {
                LOG.Debug($"Skipping local player {player.photonView.Owner.NickName}.");
                continue;
            }

            LOG.Debug($"Setting invisibility for Player {player.photonView.Owner.NickName} to {invisible}");
            SetPlayerVisibility(player, !invisible);
            if (invisible)
            {
                _hiddenPlayers.Add(player);
                GlobalHiddenPlayers.Add(player);
            }
            else
            {
                _hiddenPlayers.Remove(player);
                GlobalHiddenPlayers.Remove(player);
            }
        }
    }

    /// <summary>
    ///     Vérifie si un joueur est actuellement caché (globalement).
    ///     Utilisé par les patches pour bloquer les sons de pas.
    /// </summary>
    public static bool IsPlayerHidden(PlayerAvatar player)
    {
        return player != null && GlobalHiddenPlayers.Contains(player);
    }

    /// <summary>
    ///     Vérifie si une position correspond à un joueur caché (dans un rayon de 2m).
    /// </summary>
    public static bool IsPositionNearHiddenPlayer(Vector3 position, float radius = 2f)
    {
        foreach (var player in GlobalHiddenPlayers)
        {
            if (player == null) continue;
            var playerPos = player.transform.position;
            if (Vector3.Distance(position, playerPos) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CheckNull(PlayerAvatar player)
    {
        if (!player)
        {
            LOG.Warning("Encountered null PlayerAvatar reference, skipping.");
            return true;
        }

        if (!player.photonView || player.photonView.Owner == null)
        {
            LOG.Warning($"PlayerAvatar {player} has null photonView or Owner, skipping.");
            return true;
        }

        return false;
    }

    private void LateCheckDeactivateVisuals()
    {
        // Vérifie si le jeu a réactivé les éléments visuels des joueurs cachés et les re-désactive si nécessaire.
        if (!IsActive || _hiddenPlayers.Count == 0) return;
        foreach (var player in _hiddenPlayers)
        {
            if (!player) continue;

            // Flashlight
            if (player.flashlightController)
            {
                var flashlight = player.flashlightController;
                var flashlightMeshEnable = flashlight.mesh && flashlight.mesh.enabled;
                var flashlightSpotlightEnabled = flashlight.spotlight && flashlight.spotlight.enabled;
                var haloEnabled = flashlight.halo && flashlight.halo.enabled;
                var needToDeactivateFlashlight = flashlightMeshEnable || flashlightSpotlightEnabled || haloEnabled;
                if (needToDeactivateFlashlight)
                {
                    ToggleFlashlight(player, false);
                    LOG.Debug($"Re-deactivated flashlight for Player {player.photonView.Owner.NickName} in LateUpdate.");
                }
            }

            // Map Tool - désactiver le VisualTransform si le controller l'a réactivé
            var mapToolController = GetMapToolController(player);
            if (mapToolController != null && mapToolController.VisualTransform &&
                mapToolController.VisualTransform.gameObject.activeSelf)
            {
                mapToolController.VisualTransform.gameObject.SetActive(false);
                LOG.Debug($"Re-deactivated Map Tool VisualTransform for Player {player.photonView.Owner.NickName} in LateUpdate.");
            }
        }
    }

    private void SetPlayerVisibility(PlayerAvatar player, bool visible)
    {
        // 1. Mesh principal (corps du joueur)
        ToggleMesh(player, visible);
        // 2. Nametag au-dessus de la tête
        ToggleNameplate(player, visible);
        // 3. Lampe torche (lumière + mesh)
        ToggleFlashlight(player, visible);
        // 4. Voice chat (optionnel - pour ne plus entendre les autres)
        ToggleVoiceCom(player, visible);
        // 5. Map Tool (objet pour regarder la carte)
        ToggleMapTool(player, visible);
    }

    private static void ToggleFlashlight(PlayerAvatar player, bool visible)
    {
        LOG.Debug($"Set flashlight visibility for Player {player.photonView.Owner.NickName} to {visible}");
        if (!player.flashlightController) return;
        player.flashlightController.spotlight.enabled = visible;
        player.flashlightController.mesh.enabled = visible;
        if (player.flashlightController.halo)
        {
            player.flashlightController.halo.enabled = visible;
        }
    }

    private static void ToggleMesh(PlayerAvatar player, bool visible)
    {
        LOG.Debug($"Set mesh visibility for Player {player.photonView.Owner.NickName} to {visible}");
        if (player.playerAvatarVisuals ? player.playerAvatarVisuals.meshParent : null)
        {
            player.playerAvatarVisuals.meshParent.SetActive(visible);
        }
    }

    private static void ToggleNameplate(PlayerAvatar player, bool visible)
    {
        LOG.Debug($"Set nameplate visibility for Player {player.photonView.Owner.NickName} to {visible}");
        if (player.worldSpaceUIPlayerName)
        {
            player.worldSpaceUIPlayerName.gameObject.SetActive(visible);
        }
    }

    private static void ToggleVoiceCom(PlayerAvatar player, bool visible)
    {
        LOG.Debug($"Set voice chat mute for Player {player.photonView.Owner.NickName} to {visible}");
        if (player.voiceChat ? player.voiceChat.audioSource : null)
        {
            player.voiceChat.audioSource.mute = !visible;
        }
    }

    private void ToggleMapTool(PlayerAvatar player, bool visible)
    {
        var mapToolController = GetMapToolController(player);
        if (mapToolController == null) return;

        LOG.Debug($"Set Map Tool visibility for Player {player.photonView.Owner.NickName} to {visible}");

        // Désactiver/réactiver le VisualTransform (ce qui est affiché visuellement)
        if (mapToolController.VisualTransform)
        {
            mapToolController.VisualTransform.gameObject.SetActive(visible);
        }
    }

    private MapToolController? GetMapToolController(PlayerAvatar player)
    {
        if (_mapToolCache.TryGetValue(player, out var cached))
        {
            return cached;
        }

        // Le MapToolController n'est pas un enfant du PlayerAvatar, il faut chercher dans la scène
        // et le matcher via sa propriété PlayerAvatar
        var allMapToolControllers = FindObjectsOfType<MapToolController>(true);
        foreach (var controller in allMapToolControllers)
        {
            if (controller.PlayerAvatar == player)
            {
                _mapToolCache[player] = controller;
                LOG.Debug($"Cached MapToolController for Player {player.photonView.Owner.NickName}");
                return controller;
            }
        }

        LOG.Warning($"MapToolController not found for Player {player.photonView.Owner.NickName}");
        return null;
    }
}