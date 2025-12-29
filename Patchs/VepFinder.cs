#nullable disable
using Photon.Pun;
using UnityEngine;
using VepMod.Enemies.Whispral;
using VepMod.VepFramework;

namespace VepMod.Patchs;

public class VepFinder : MonoBehaviour
{
    private static VepFinder _instance;
    private static bool _initialized;
    private static readonly VepLogger LOG = VepLogger.Create<VepFinder>();

    public static WhispralMimics LocalMimics { get; set; }

    private void OnDestroy()
    {
        if (!_initialized)
        {
            return;
        }

        LocalMimics = null;
        _instance = null;
        LOG.Info("VepFinder destroyed, clearing cache.");
    }

    public static void EnsureInitialized()
    {
        if (_instance)
        {
            return;
        }


        _instance = new GameObject(nameof(VepFinder)).AddComponent<VepFinder>();
        _initialized = true;
        DontDestroyOnLoad(_instance.gameObject);
        var localPlayer = PhotonNetwork.LocalPlayer;
        LOG.Info($"MimicsFinder initialized for Player {localPlayer?.ActorNumber ?? -1}");
    }
}