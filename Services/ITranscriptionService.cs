using TranscriptionPipeline.Models;

namespace TranscriptionPipeline.Services;

public interface ITranscriptionService
{
    Task<TranscriptionResult> TranscribeFileAsync(string filePath, TranscriptionOptions? options = null, CancellationToken cancellationToken = default);
    Task<TranscriptionResult> TranscribeStreamAsync(Stream audioStream, TranscriptionOptions? options = null, CancellationToken cancellationToken = default);
}
