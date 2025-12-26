using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace VepMod.Enemies.Whispral;

public sealed class InvisibleDebuff : MonoBehaviour
{
    private static readonly ManualLogSource LOG = Logger.CreateLogSource("VepMod.InvisibleDebuff");
    private readonly List<PlayerAvatar> hiddenPlayers = new();
    public bool IsActive { get; private set; }

    public void ApplyMadness(bool enable)
    {
        IsActive = enable;

        foreach (var player in GameDirector.instance.PlayerList)
        {
            // Skip le joueur local (celui qui subit le debuff)
            if (player.photonView.IsMine)
            {
                continue;
            }

            LOG.LogInfo($"Setting invisibility for Player {player.photonView.Owner.ActorNumber} to {enable}");
            SetPlayerVisibility(player, !enable);
            if (enable)
            {
                hiddenPlayers.Add(player);
            }
            else
            {
                hiddenPlayers.Remove(player);
            }
        }
    }

    private static void HideFlashlight(PlayerAvatar player, bool visible)
    {
        if (player.flashlightController != null)
        {
            player.flashlightController.spotlight.enabled = visible;
            player.flashlightController.mesh.enabled = visible;
            if (player.flashlightController.halo != null)
            {
                player.flashlightController.halo.enabled = visible;
            }
        }
    }

    private static void HideMesh(PlayerAvatar player, bool visible)
    {
        if (player.playerAvatarVisuals?.meshParent != null)
        {
            player.playerAvatarVisuals.meshParent.SetActive(visible);
        }
    }

    private static void HideNameplate(PlayerAvatar player, bool visible)
    {
        if (player.worldSpaceUIPlayerName != null)
        {
            player.worldSpaceUIPlayerName.gameObject.SetActive(visible);
        }
    }

    private static void MuteVoiceCom(PlayerAvatar player, bool visible)
    {
        if (player.voiceChat?.audioSource != null)
        {
            player.voiceChat.audioSource.mute = !visible;
        }
    }

    private static void SetPlayerVisibility(PlayerAvatar player, bool visible)
    {
        // 1. Mesh principal (corps du joueur)
        HideMesh(player, visible);
        LOG.LogInfo($"Set mesh visibility for Player {player.photonView.Owner.ActorNumber} to {visible}");

        // 2. Nametag au-dessus de la tête
        HideNameplate(player, visible);
        LOG.LogInfo($"Set nameplate visibility for Player {player.photonView.Owner.ActorNumber} to {visible}");

        // 3. Lampe torche (lumière + mesh)
        HideFlashlight(player, visible);
        LOG.LogInfo($"Set flashlight visibility for Player {player.photonView.Owner.ActorNumber} to {visible}");

        // 4. Voice chat (optionnel - pour ne plus entendre les autres)
        MuteVoiceCom(player, visible);
        LOG.LogInfo($"Set voice chat mute for Player {player.photonView.Owner.ActorNumber} to {visible}");
    }
}