using System.Text.Json;
using TranscriptionPipeline.Models;
using TranscriptionPipeline.Services;

namespace TranscriptionPipeline;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Audio Transcription Utility");
        Console.WriteLine("---------------------------");

        string audioPath = string.Empty;
        string defaultModel = File.Exists(ResolvePath("ggml-tiny.bin")) ? "ggml-tiny.bin" : "ggml-base.bin";
        string modelPath = ResolvePath(defaultModel);

        // Select audio file from arguments or interactive prompt
        if (args.Length > 0)
        {
            audioPath = ResolvePath(args[0]);
            if (args.Length > 1) modelPath = ResolvePath(args[1]);
        }
        else
        {
            Console.WriteLine("Please place your audio file (.mp3, .wav, .m4a) in the project directory.");

            var searchFolder = Directory.GetCurrentDirectory();
            var files = new List<string>();

            // Use centralized audio search patterns (DRY)
            foreach (var pattern in AudioFormats.SearchPatterns)
            {
                files.AddRange(Directory.GetFiles(searchFolder, pattern));

                // Also check project directory if running from bin\Debug
                var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
                if (Directory.Exists(projectDir) && projectDir != searchFolder)
                {
                    files.AddRange(Directory.GetFiles(projectDir, pattern));
                }
            }

            var uniqueFiles = files.Distinct().ToList();

            if (uniqueFiles.Count > 0)
            {
                Console.WriteLine("\nDetected audio files:");
                for (int i = 0; i < uniqueFiles.Count; i++)
                {
                    Console.WriteLine($"  [{i + 1}] {Path.GetFileName(uniqueFiles[i])}");
                }

                Console.Write($"\nSelect file [1-{uniqueFiles.Count}] (default is 1): ");
                string? input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input) || input == "1")
                    audioPath = uniqueFiles[0];
                else if (int.TryParse(input, out int index) && index >= 1 && index <= uniqueFiles.Count)
                    audioPath = uniqueFiles[index - 1];
                else
                    audioPath = ResolvePath(input);
            }
            else
            {
                Console.Write("\nEnter audio file name or full path: ");
                audioPath = ResolvePath(Console.ReadLine()?.Trim() ?? string.Empty);
            }
        }

        var options = new TranscriptionOptions { ModelPath = modelPath, Language = "auto" };
        ITranscriptionService service = new WhisperTranscriptionService();

        // Traceable background job worker (prevents timeout on long audio files)
        await using var jobWorker = new TranscriptionJobWorker(service);

        try
        {
            Console.WriteLine($"\n[TEST] Using Model: {Path.GetFileName(modelPath)}");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Enqueue job immediately without blocking caller
            var jobId = jobWorker.Enqueue(audioPath, options);
            Console.WriteLine($"Job enqueued. Traceable Job ID: {jobId}");
            Console.Write("Processing in background");

            // Monitor background job progress
            TranscriptionJob? job = null;
            while (true)
            {
                job = jobWorker.GetJob(jobId);
                if (job != null && (job.Status == JobStatus.Completed || job.Status == JobStatus.Failed))
                    break;

                Console.Write(".");
                await Task.Delay(500);
            }
            stopwatch.Stop();
            Console.WriteLine();
            Console.WriteLine($"\n[TEST] Execution Time: {stopwatch.Elapsed.TotalSeconds:F2} seconds ({stopwatch.Elapsed.Minutes}m {stopwatch.Elapsed.Seconds}s)");

            if (job?.Status == JobStatus.Completed && job.Result != null)
            {
                Console.WriteLine("\nTranscript:");
                foreach (var segment in job.Result.Segments)
                {
                    Console.WriteLine($"[{segment.Start:mm\\:ss} -> {segment.End:mm\\:ss}] {segment.Text}");
                }

                // Export result to json file
                string outputPath = Path.Combine(AppContext.BaseDirectory, "transcription_output.json");
                await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(job.Result, new JsonSerializerOptions { WriteIndented = true }));

                Console.WriteLine($"\nSaved transcript to: {outputPath}");
                return 0;
            }
            else
            {
                Console.WriteLine($"Error: {job?.ErrorMessage ?? "Job failed."}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    // Resolves file path from current folder, base directory, or project root
    private static string ResolvePath(string path)
    {
        if (File.Exists(path)) return Path.GetFullPath(path);

        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var projectPath = Path.Combine(projectDir, path);
        if (File.Exists(projectPath)) return projectPath;

        var baseDirPath = Path.Combine(AppContext.BaseDirectory, path);
        if (File.Exists(baseDirPath)) return baseDirPath;

        return path;
    }
}
