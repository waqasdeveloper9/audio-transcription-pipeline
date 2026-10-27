using System.Text;
using TranscriptionPipeline.Models;
using Whisper.net;

namespace TranscriptionPipeline.Services;

public class WhisperTranscriptionService : ITranscriptionService
{
    public async Task<TranscriptionResult> TranscribeFileAsync(string filePath, TranscriptionOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Verify file exists
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found.", filePath);

        // Check supported audio format
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension) || !AudioFormats.IsSupported(extension))
            throw new NotSupportedException($"Unsupported format '{extension}'. Supported: {string.Join(", ", AudioFormats.SupportedExtensions)}");

        // Convert audio to 16kHz mono WAV if needed
        string processingPath = filePath;
        bool isTemporaryFile = false;

        try
        {
            if (!extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) || NeedsResampling(filePath))
            {
                processingPath = ConvertToCanonical16KhzWav(filePath);
                isTemporaryFile = true;
            }

            await using var fileStream = new FileStream(processingPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            return await TranscribeStreamAsync(fileStream, options, cancellationToken);
        }
        finally
        {
            // Clean up temporary file
            if (isTemporaryFile && File.Exists(processingPath))
            {
                try { File.Delete(processingPath); } catch { }
            }
        }
    }

    // Check if wave file has expected sample rate and channels
    private static bool NeedsResampling(string wavFilePath)
    {
        try
        {
            using var reader = new NAudio.Wave.AudioFileReader(wavFilePath);
            return reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1;
        }
        catch
        {
            return true;
        }
    }

    // Convert audio to 16kHz mono 16-bit WAV
    private static string ConvertToCanonical16KhzWav(string inputFilePath)
    {
        string tempWavPath = Path.Combine(Path.GetTempPath(), $"audio_{Guid.NewGuid():N}.wav");

        using var reader = new NAudio.Wave.AudioFileReader(inputFilePath);
        var targetFormat = new NAudio.Wave.WaveFormat(16000, 16, 1);

        using var resampler = new NAudio.Wave.MediaFoundationResampler(reader, targetFormat) { ResamplerQuality = 60 };
        NAudio.Wave.WaveFileWriter.CreateWaveFile(tempWavPath, resampler);
        return tempWavPath;
    }

    public async Task<TranscriptionResult> TranscribeStreamAsync(Stream audioStream, TranscriptionOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new TranscriptionOptions();

        if (audioStream == null || audioStream.Length == 0)
            throw new ArgumentException("Audio stream is empty or null.", nameof(audioStream));

        // Verify model file existence
        if (!File.Exists(options.ModelPath))
        {
            var fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", options.ModelPath));
            if (File.Exists(fallback)) options.ModelPath = fallback;
        }

        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException($"Whisper model binary not found at '{options.ModelPath}'. Ensure ggml-base.bin is present.");

        using var factory = WhisperFactory.FromPath(options.ModelPath);
        using var processor = factory.CreateBuilder().WithLanguage(options.Language).Build();

        var segments = new List<AudioSegment>();
        var fullTextBuilder = new StringBuilder();
        int segmentIndex = 1;
        TimeSpan maxEndTime = TimeSpan.Zero;

        await foreach (var segment in processor.ProcessAsync(audioStream, cancellationToken))
        {
            var cleanText = segment.Text.Trim();
            if (string.IsNullOrWhiteSpace(cleanText))
                continue;

            segments.Add(new AudioSegment(segmentIndex++, segment.Start, segment.End, cleanText, segment.Probability));
            fullTextBuilder.Append(cleanText).Append(' ');

            if (segment.End > maxEndTime)
                maxEndTime = segment.End;
        }

        return new TranscriptionResult(options.Language, maxEndTime, fullTextBuilder.ToString().Trim(), segments);
    }
}
