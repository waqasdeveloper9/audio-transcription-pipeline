# Audio Transcription Pipeline

A lightweight speech-to-text pipeline built with **.NET 8** and **C#**, utilizing local open-source Whisper models (`Whisper.net`) with segment-level timestamping, automatic audio normalization, and traceable background job processing.

---

## Key Engineering Decisions

### 1. Audio Format Normalization
Speech recognition models expect raw audio in a canonical format: **16kHz, 16-bit, Mono WAV**. 
* Rather than rejecting formats like MP3, M4A, or stereo WAV, the pipeline automatically detects the format using `AudioFormats` and normalizes it on the fly using `NAudio` (Windows Media Foundation).
* The normalized file is processed and cleaned up in a `finally` block to prevent disk bloat.

### 2. Traceable Background Processing (No Timeouts)
For long audio files (e.g., 1-2 hours), processing audio synchronously causes connection drops and gateway timeouts.
* We decouple ingestion from processing using `TranscriptionJobWorker` backed by `System.Threading.Channels`.
* Enqueueing a file immediately generates a traceable `JobId` (`Guid`) and returns without blocking.
* Jobs transition cleanly through states: `Queued` -> `Processing` -> `Completed` (or `Failed`).

### 3. Segment Timestamps & Streaming
* Audio is processed asynchronously via `IAsyncEnumerable` (`await foreach`).
* As speech is recognized in 30-second sliding windows, each sentence is mapped to an `AudioSegment` record with precise `Start` and `End` timestamps (`TimeSpan`).
* The final output is aggregated into a structured `TranscriptionResult` and exported to `transcription_output.json`.

### 4. Enterprise Storage Architecture (Design)
* **Audio Storage:** Heavy audio binaries are stored in Object Storage (Azure Blob / AWS S3) with lifecycle rules (e.g. auto-deletion after 7-30 days to optimize cost). Only the URL is stored in the database.
* **Transcript Storage:** Structured transcripts and segment metadata are stored in PostgreSQL or MongoDB, with full-text indexing for rapid search across recordings.

### 5. Failure Recovery & Retry Strategy
* **Transient Errors:** Automatic retries with exponential backoff via message queue (RabbitMQ / Azure Service Bus).
* **Corrupt Files:** Moved to a Dead Letter Queue (DLQ) after 3 attempts with an error message.
* **Long Audio Checkpointing:** Intermediate progress is saved per chunk so retries resume from the last completed segment rather than starting over.

### 6. Public API Design
* **Asynchronous Contract:** `POST /v1/transcriptions` returns `202 Accepted` with a `JobId`.
* **Delivery:** Clients can poll `GET /v1/transcriptions/{id}` or provide a `webhook_url` for automatic push notification.
* **Security & Governance:** Hashed API Keys, rate limiting (`429 Too Many Requests`), and URL versioning (`/v1/`).

---

## Project Structure

```text
TranscriptionPipeline/
│
├── Models/
│   └── TranscriptionModels.cs        # AudioFormats, AudioSegment, TranscriptionResult, TranscriptionJob
│
├── Services/
│   ├── ITranscriptionService.cs      # Service interface for audio transcription
│   ├── WhisperTranscriptionService.cs# Whisper.net implementation with format normalization
│   └── TranscriptionJobWorker.cs     # Channel-based traceable background worker
│
├── Program.cs                        # CLI entrypoint with interactive file picker and model auto-download
├── run.bat                           # 1-click execution script
└── README.md                         # Architecture and design documentation
```

---

## Getting Started

### Prerequisites
* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Running the Application

1. Open your terminal in the project directory:
   ```bash
   cd TranscriptionPipeline
   ```

2. Run the application:
   ```bash
   dotnet run
   ```
   *(Or simply double-click `run.bat` on Windows).*

3. If running for the first time, the utility will automatically download the official `ggml-base.bin` model (~140MB).

4. Select an available audio file (`jfk.wav` or `Test1.mp3`) from the interactive prompt, or type any file name.
