using System;
using System.Collections.Generic;
using System.Linq;
using VepMod.Enemies.Whispral;
using Xunit;

namespace VoiceBank.Tests;

public class VoiceSampleChunksTests
{
    private static byte[] Wav(int length, byte seed = 0)
    {
        return Enumerable.Range(0, length).Select(i => (byte)(i + seed)).ToArray();
    }

    private static byte[]? SendAll(VoiceSampleChunks receiver, List<byte[]> chunks)
    {
        byte[]? wav = null;
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Null(wav);
            wav = receiver.Add(chunks[i], i, chunks.Count);
        }

        return wav;
    }

    [Fact]
    public void SplitThenReassembleGivesBackTheSameBytes()
    {
        var wav = Wav(VoiceSampleChunks.ChunkSizeBytes * 2 + 100);

        var chunks = VoiceSampleChunks.Split(wav);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks.Take(2), c => Assert.Equal(VoiceSampleChunks.ChunkSizeBytes, c.Length));
        Assert.Equal(wav, SendAll(new VoiceSampleChunks(), chunks));
    }

    [Fact]
    public void ASampleSmallerThanOneChunkTravelsInOneChunk()
    {
        var wav = Wav(44);

        var chunks = VoiceSampleChunks.Split(wav);

        Assert.Single(chunks);
        Assert.Equal(wav, new VoiceSampleChunks().Add(chunks[0], 0, 1));
    }

    [Fact]
    public void AFirstChunkRestartsTheTransfer()
    {
        var abandoned = VoiceSampleChunks.Split(Wav(VoiceSampleChunks.ChunkSizeBytes * 3, 1));
        var wav = Wav(VoiceSampleChunks.ChunkSizeBytes + 1, 2);
        var receiver = new VoiceSampleChunks();

        Assert.Null(receiver.Add(abandoned[0], 0, abandoned.Count));
        Assert.Null(receiver.Add(abandoned[1], 1, abandoned.Count));

        Assert.Equal(wav, SendAll(receiver, VoiceSampleChunks.Split(wav)));
    }

    [Fact]
    public void AChunkOutsideATransferIsDropped()
    {
        var chunks = VoiceSampleChunks.Split(Wav(VoiceSampleChunks.ChunkSizeBytes * 2));
        var receiver = new VoiceSampleChunks();

        // Joined after the first chunk went out: the last one alone completes nothing.
        Assert.Null(receiver.Add(chunks[1], 1, chunks.Count));
        Assert.Null(receiver.Add(chunks[0], 0, 0));
    }

    [Fact]
    public void InterleavedTransfersFromTwoSendersDoNotMix()
    {
        var wavA = Wav(VoiceSampleChunks.ChunkSizeBytes * 2 + 7, 3);
        var wavB = Wav(VoiceSampleChunks.ChunkSizeBytes * 3 + 9, 4);
        var chunksA = VoiceSampleChunks.Split(wavA);
        var chunksB = VoiceSampleChunks.Split(wavB);
        var fromA = new VoiceSampleChunks();
        var fromB = new VoiceSampleChunks();

        byte[]? receivedA = null, receivedB = null;
        for (var i = 0; i < Math.Max(chunksA.Count, chunksB.Count); i++)
        {
            if (i < chunksA.Count) receivedA = fromA.Add(chunksA[i], i, chunksA.Count);
            if (i < chunksB.Count) receivedB = fromB.Add(chunksB[i], i, chunksB.Count);
        }

        Assert.Equal(wavA, receivedA);
        Assert.Equal(wavB, receivedB);
    }
}
