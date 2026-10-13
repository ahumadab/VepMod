using AudioBenchmark.Models;

namespace AudioBenchmark.Validators;

/// <summary>
///     Standalone 16-bit PCM WAV reader, without Unity; keeps the first channel only.
/// </summary>
public static class WavReader
{
    /// <summary>
    ///     Samples are normalised to [-1, 1].
    /// </summary>
    public static WavFile Read(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var bytes = File.ReadAllBytes(filePath);

        if (bytes.Length < 44)
            throw new InvalidDataException($"WAV file too short ({bytes.Length} bytes): {filePath}");

        var riff = System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
        var wave = System.Text.Encoding.ASCII.GetString(bytes, 8, 4);
        if (riff != "RIFF" || wave != "WAVE")
            throw new InvalidDataException($"Not a valid WAV file: {filePath}");

        var audioFormat = BitConverter.ToInt16(bytes, 20);
        var channels = BitConverter.ToInt16(bytes, 22);
        var sampleRate = BitConverter.ToInt32(bytes, 24);
        var bitsPerSample = BitConverter.ToInt16(bytes, 34);

        if (audioFormat != 1)
            throw new InvalidDataException($"Only PCM format supported, got format {audioFormat}: {filePath}");

        var dataOffset = 12;
        var dataSize = 0;
        while (dataOffset < bytes.Length - 8)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(bytes, dataOffset, 4);
            var chunkSize = BitConverter.ToInt32(bytes, dataOffset + 4);

            if (chunkId == "data")
            {
                dataOffset += 8;
                dataSize = chunkSize;
                break;
            }

            dataOffset += 8 + chunkSize;
        }

        if (dataSize == 0)
            throw new InvalidDataException($"No data chunk found: {filePath}");

        var bytesPerSample = bitsPerSample / 8;
        var frameSize = channels * bytesPerSample;
        var frameCount = Math.Min(dataSize / frameSize, (bytes.Length - dataOffset) / frameSize);
        var samples = new float[frameCount];

        for (var i = 0; i < frameCount; i++)
        {
            var byteIdx = dataOffset + i * frameSize;
            if (byteIdx + 1 >= bytes.Length) break;

            var sample16 = BitConverter.ToInt16(bytes, byteIdx);
            samples[i] = sample16 / 32768f;
        }

        var playerName = ExtractPlayerName(filePath);
        var duration = (float)samples.Length / sampleRate;

        return new WavFile
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            PlayerName = playerName,
            SampleRate = sampleRate,
            SampleCount = samples.Length,
            DurationSeconds = duration,
            Samples = samples,
            FileSizeBytes = fileInfo.Length
        };
    }

    /// <summary>
    ///     Reads the player name from the parent folder, named player_NickName.
    /// </summary>
    private static string ExtractPlayerName(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        var folderName = Path.GetFileName(dir) ?? "unknown";
        return folderName.StartsWith("player_") ? folderName[7..] : folderName;
    }

    /// <summary>
    ///     Recurses into the player_* subfolders.
    /// </summary>
    public static List<WavFile> LoadDataset(string basePath)
    {
        var results = new List<WavFile>();
        var errors = new List<string>();

        var wavFiles = Directory.GetFiles(basePath, "*.wav", SearchOption.AllDirectories);

        foreach (var file in wavFiles)
        {
            try
            {
                results.Add(Read(file));
            }
            catch (Exception ex)
            {
                errors.Add($"  SKIP {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            Console.WriteLine($"\n  Warnings: {errors.Count} files skipped:");
            foreach (var e in errors.Take(10))
                Console.WriteLine(e);
            if (errors.Count > 10)
                Console.WriteLine($"  ... and {errors.Count - 10} more");
        }

        return results;
    }
}
