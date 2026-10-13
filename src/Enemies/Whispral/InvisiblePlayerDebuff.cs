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
    ///     Across every Victim on this client.
    /// </summary>
    public static bool IsPlayerHidden(PlayerAvatar player)
    {
        return player != null && GlobalHiddenPlayers.Contains(player);
    }

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
        // The game turns some of a hidden player's visuals back on; they are hidden again after it.
        if (!IsActive || _hiddenPlayers.Count == 0) return;
        foreach (var player in _hiddenPlayers)
        {
            if (!player) continue;

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
        ToggleMesh(player, visible);
        ToggleNameplate(player, visible);
        ToggleFlashlight(player, visible);
        ToggleVoiceCom(player, visible);
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

        // A MapToolController is not under its PlayerAvatar in the hierarchy: it is found in the scene by its
        // PlayerAvatar property.
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
