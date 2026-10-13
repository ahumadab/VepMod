namespace AudioBenchmark.Models;

public sealed class WavFile
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string PlayerName { get; init; }
    public required int SampleRate { get; init; }
    public required int SampleCount { get; init; }
    public required float DurationSeconds { get; init; }
    public required float[] Samples { get; init; }
    public required long FileSizeBytes { get; init; }
}
