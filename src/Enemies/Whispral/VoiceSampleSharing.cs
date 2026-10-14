using System;
using System.Collections;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;
using VepMod.VepFramework;
using Random = UnityEngine.Random;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     On every player avatar. The local player's asks the recorder for Captures, keeps the Voice Samples and shares them
///     with the other clients; every other one receives the Voice Samples of its avatar's owner.
/// </summary>
public sealed class VoiceSampleSharing : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<VoiceSampleSharing>(true);

    private static readonly WaitForSeconds ChunkDelay = new(0.125f);

    // One sender per avatar: its owner. The avatar goes with a player who leaves, and their transfer with it.
    private readonly VoiceSampleChunks _received = new();

    private PhotonView _photonView;
    private PlayerAvatar _playerAvatar;

#if DEBUG
    /// <summary>Shown on the DroidDevTools HUD.</summary>
    public static string? LastCaptureVerdict { get; private set; }
#endif

    private void Awake()
    {
        _photonView = GetComponent<PhotonView>();
        _playerAvatar = GetComponent<PlayerAvatar>();
    }

    private IEnumerator Start()
    {
        if (!_photonView.IsMine) yield break;

        while (!_playerAvatar.voiceChat)
        {
            yield return null;
        }

        if (!SemiFunc.RunIsLevel()) yield break;

        LOG.Info($"Voice Sample sharing started for the local player {_playerAvatar.VoiceBankKey()}.");
        StartCoroutine(RequestCaptures());
        yield return SaveAndShareCaptures();
    }

    private static IEnumerator RequestCaptures()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(VepMod.ShareDelay.Min, VepMod.ShareDelay.Max));
            VepMod.VoiceRecorder.RequestCapture();
        }
        // ReSharper disable once IteratorNeverReturns
    }

    // The VAD and the disk write would stall the main thread, so they run in a background task.
    private IEnumerator SaveAndShareCaptures()
    {
        // A request or a Capture left from the previous level, or the lobby, is not this level's to share.
        VepMod.VoiceRecorder.Clear();
        var player = _playerAvatar.VoiceBankKey();
        while (true)
        {
            if (!VepMod.VoiceRecorder.TryTakeCapture(out var capture))
            {
                yield return null;
                continue;
            }

            var minDuration = VepMod.ConfigAudioMinDuration.Value;
            var saving = Task.Run(() => ValidateAndSave(capture, player, minDuration));
            while (!saving.IsCompleted)
            {
                yield return null;
            }

            var (verdict, wav) = saving.Result;
#if DEBUG
            LastCaptureVerdict = verdict;
#endif
            if (wav != null)
            {
                yield return Share(wav, player);
            }
        }
        // ReSharper disable once IteratorNeverReturns
    }

    /// <summary>Runs in a background task.</summary>
    /// <returns>The verdict for the HUD, and the WAV to share, null when the Capture is not a Voice Sample.</returns>
    private static (string verdict, byte[]? wav) ValidateAndSave(Capture capture, PlayerKey player, float minDuration)
    {
        var verdict = "not validated";
        try
        {
            var (accepted, detail) = CaptureValidation.Validate(capture, minDuration, VepMod.SpeechCheck);
            verdict = $"{(accepted ? "accepted" : "rejected")} " +
                      $"({capture.DurationSeconds:F2} s at {capture.Sample.SampleRate} Hz): {detail}";
            if (!accepted)
            {
                LOG.Debug($"Capture {verdict}");
                return (verdict, null);
            }

            VepMod.VoiceBanks.Save(player, capture.Sample);
            var wav = WavCodec.Encode(capture.Sample);
            LOG.Debug($"Capture {verdict}. Voice Sample saved in the Voice Bank of {player}.");
            return (verdict, wav);
        }
        catch (Exception ex)
        {
            LOG.Error($"Error validating or saving a Capture of {player}: {ex.Message}");
            return (verdict, null);
        }
    }

    private IEnumerator Share(byte[] wav, PlayerKey player)
    {
        var chunks = VoiceSampleChunks.Split(wav);
        LOG.Debug($"Sharing a Voice Sample of {player}: {wav.Length} bytes in {chunks.Count} chunks.");

        for (var i = 0; i < chunks.Count; i++)
        {
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                LOG.Warning("Photon not connected, Voice Sample sharing aborted.");
                yield break;
            }

            _photonView.RPC(nameof(ReceiveVoiceSampleChunk), RpcTarget.Others, chunks[i], i, chunks.Count);
            yield return ChunkDelay;
        }

        LOG.Debug($"Voice Sample of {player} shared: {chunks.Count} chunks sent.");
    }

    // The sender is the avatar's owner: the chunk names no player (ADR-0006).
    [PunRPC]
    public void ReceiveVoiceSampleChunk(byte[] chunk, int chunkIndex, int chunkCount, PhotonMessageInfo info)
    {
        if (info.Sender != _photonView.Owner) return;

        var wav = _received.Add(chunk, chunkIndex, chunkCount);
        if (wav == null) return;

        var player = _playerAvatar.VoiceBankKey();
        LOG.Debug($"Voice Sample received from {player}: {wav.Length} bytes in {chunkCount} chunks.");
        SaveReceived(wav, player);
    }

    private static async void SaveReceived(byte[] wav, PlayerKey player)
    {
        try
        {
            await Task.Run(() => VepMod.VoiceBanks.Save(player, WavCodec.Decode(wav)));
            LOG.Debug($"Voice Sample received from {player} saved in their Voice Bank.");
        }
        catch (Exception ex)
        {
            LOG.Error($"Error saving the Voice Sample received from {player}: {ex.Message}");
        }
    }
}
