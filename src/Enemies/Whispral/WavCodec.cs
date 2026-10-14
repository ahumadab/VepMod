using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace VepMod.Enemies.Whispral;

public sealed class VoiceSample(float[] samples, int sampleRate)
{
    /// <summary>Mono, in [-1, 1].</summary>
    public float[] Samples { get; } = samples;

    public int SampleRate { get; } = sampleRate;
}

/// <summary>
///     The file format of a Voice Sample, on disk and on the network: a 16-bit PCM mono WAV.
/// </summary>
public static class WavCodec
{
    private const int HeaderSize = 44;
    private const short PcmFormat = 1;
    private const short Channels = 1;
    private const short BitsPerSample = 16;
    private const short BytesPerSample = BitsPerSample / 8;
    /// <summary>Of a 16-bit PCM sample.</summary>
    internal const float FullScale = 32768f;

    public static byte[] Encode(VoiceSample sample)
    {
        var dataSize = sample.Samples.Length * BytesPerSample;
        var wav = new byte[HeaderSize + dataSize];
        var span = wav.AsSpan();

        WriteId(span, 0, "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], HeaderSize - 8 + dataSize);
        WriteId(span, 8, "WAVE");
        WriteId(span, 12, "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], PcmFormat);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sample.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sample.SampleRate * BytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], BytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], BitsPerSample);
        WriteId(span, 36, "data");
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);

        for (var i = 0; i < sample.Samples.Length; i++)
        {
            var pcm = Math.Clamp((int)Math.Round(sample.Samples[i] * FullScale), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(span[(HeaderSize + i * BytesPerSample)..], (short)pcm);
        }

        return wav;
    }

    /// <exception cref="InvalidDataException">Not a complete 16-bit PCM mono WAV.</exception>
    public static VoiceSample Decode(byte[] wav)
    {
        if (wav.Length < 12 || ReadId(wav, 0) != "RIFF" || ReadId(wav, 8) != "WAVE")
        {
            throw new InvalidDataException("Not a WAV file.");
        }

        int? sampleRate = null;
        var offset = 12;
        while (offset + 8 <= wav.Length)
        {
            var id = ReadId(wav, offset);
            var size = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(offset + 4));
            var body = offset + 8;
            if (size < 0 || size > wav.Length - body)
            {
                throw new InvalidDataException($"WAV chunk '{id}' runs past the end of the file.");
            }

            var chunk = wav.AsSpan(body, size);
            if (id == "fmt ")
            {
                sampleRate = ReadFormat(chunk);
            }
            else if (id == "data")
            {
                if (sampleRate == null) throw new InvalidDataException("WAV data chunk before its fmt chunk.");
                return new VoiceSample(ReadSamples(chunk), sampleRate.Value);
            }

            // RIFF pads every chunk to an even size.
            offset = body + size + (size & 1);
        }

        throw new InvalidDataException("WAV file without a data chunk.");
    }

    private static int ReadFormat(ReadOnlySpan<byte> fmt)
    {
        if (fmt.Length < 16) throw new InvalidDataException("WAV fmt chunk too short.");

        var format = BinaryPrimitives.ReadInt16LittleEndian(fmt);
        var channels = BinaryPrimitives.ReadInt16LittleEndian(fmt[2..]);
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(fmt[4..]);
        var bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(fmt[14..]);
        if (format != PcmFormat || channels != Channels || bitsPerSample != BitsPerSample || sampleRate <= 0)
        {
            throw new InvalidDataException(
                $"Not a 16-bit PCM mono WAV (format {format}, {channels} channels, {bitsPerSample} bits, {sampleRate} Hz).");
        }

        return sampleRate;
    }

    private static float[] ReadSamples(ReadOnlySpan<byte> data)
    {
        var samples = new float[data.Length / BytesPerSample];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data[(i * BytesPerSample)..]) / FullScale;
        }

        return samples;
    }

    private static void WriteId(Span<byte> wav, int offset, string id)
    {
        Encoding.ASCII.GetBytes(id, wav[offset..]);
    }

    private static string ReadId(byte[] wav, int offset)
    {
        return Encoding.ASCII.GetString(wav, offset, 4);
    }
}
