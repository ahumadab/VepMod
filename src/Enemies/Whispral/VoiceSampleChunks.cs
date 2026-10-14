using System;
using System.Collections.Generic;
using System.Linq;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Cuts a Voice Sample's WAV into chunks small enough for one RPC, and puts one sender's chunks back together.
/// </summary>
public sealed class VoiceSampleChunks
{
    public const int ChunkSizeBytes = 8192;

    private List<byte[]>? _received;
    private int _expectedCount;

    public static List<byte[]> Split(byte[] wav)
    {
        var chunks = new List<byte[]>();
        for (var offset = 0; offset < wav.Length; offset += ChunkSizeBytes)
        {
            chunks.Add(wav.AsSpan(offset, Math.Min(ChunkSizeBytes, wav.Length - offset)).ToArray());
        }

        return chunks;
    }

    /// <summary>
    ///     Chunks must come in index order, as Photon delivers one PhotonView's RPCs. A first chunk restarts the
    ///     transfer; a chunk that does not follow the current one ends it.
    /// </summary>
    /// <returns>The complete WAV on its last chunk, null otherwise.</returns>
    public byte[]? Add(byte[] chunk, int index, int count)
    {
        if (index == 0)
        {
            _received = new List<byte[]>();
            _expectedCount = count;
        }

        if (_received == null || count <= 0 || count != _expectedCount || index != _received.Count)
        {
            _received = null;
            return null;
        }

        _received.Add(chunk);
        if (_received.Count < _expectedCount) return null;

        var wav = _received.SelectMany(c => c).ToArray();
        _received = null;
        return wav;
    }
}
