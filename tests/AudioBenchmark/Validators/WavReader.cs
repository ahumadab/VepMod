using AudioBenchmark.Models;
using VepMod.Enemies.Whispral;

namespace AudioBenchmark.Validators;

/// <summary>
///     Reads the Voice Samples of a Voice Bank folder with the plugin's own codec.
/// </summary>
public static class WavReader
{
    public static WavFile Read(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var sample = WavCodec.Decode(bytes);

        return new WavFile
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            PlayerName = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "unknown",
            SampleRate = sample.SampleRate,
            SampleCount = sample.Samples.Length,
            DurationSeconds = (float)sample.Samples.Length / sample.SampleRate,
            Samples = sample.Samples,
            FileSizeBytes = bytes.Length
        };
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
