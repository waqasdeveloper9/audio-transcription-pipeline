namespace TranscriptionPipeline.Models;

public static class AudioFormats
{
    public static readonly string[] SupportedExtensions = [".wav", ".mp3", ".m4a", ".ogg", ".flac"];
    public static readonly string[] SearchPatterns = SupportedExtensions.Select(ext => $"*{ext}").ToArray();

    public static bool IsSupported(string extension) => SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}

public enum JobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public class TranscriptionJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FilePath { get; init; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public TranscriptionOptions Options { get; init; } = new();
    public TranscriptionResult? Result { get; set; }
    public string? ErrorMessage { get; set; }
}

public record AudioSegment(int Index, TimeSpan Start, TimeSpan End, string Text, float Probability = 0f);
public record TranscriptionResult(string DetectedLanguage, TimeSpan Duration, string FullText, IReadOnlyList<AudioSegment> Segments);

public class TranscriptionOptions
{
    public string ModelPath { get; set; } = "ggml-base.bin";
    public string Language { get; set; } = "auto";
}
