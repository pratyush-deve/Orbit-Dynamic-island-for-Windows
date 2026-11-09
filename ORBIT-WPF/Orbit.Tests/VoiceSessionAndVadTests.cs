using System;
using System.Threading;
using Orbit.Services.Voice;
using Whisper.net;
using Whisper.net.Ggml;
using Xunit;

namespace Orbit.Tests;

public class VoiceSessionAndVadTests
{
    private class FakeAudioCaptureService : IAudioCaptureService
    {
        public bool IsCapturing { get; private set; }
        public bool StartCaptureResult { get; set; } = true;
        public byte[] FakeData { get; set; } = new byte[] { 1, 2, 3, 4 };

        public event EventHandler<float>? AudioLevelUpdated;
        public event EventHandler<string>? CaptureErrorOccurred;

        public bool StartCapture()
        {
            if (!StartCaptureResult)
            {
                CaptureErrorOccurred?.Invoke(this, "Microphone device not found");
                return false;
            }

            IsCapturing = true;
            return true;
        }

        public byte[] StopCapture()
        {
            IsCapturing = false;
            return FakeData;
        }

        public void CancelCapture()
        {
            IsCapturing = false;
        }

        public void EmitLevel(float intensity)
        {
            AudioLevelUpdated?.Invoke(this, intensity);
        }

        public void Dispose()
        {
            IsCapturing = false;
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task WhisperDownloader_DownloadsTinyEnModelSuccessfully()
    {
        string modelsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ORBIT", "models");
        System.IO.Directory.CreateDirectory(modelsDir);
        string modelPath = System.IO.Path.Combine(modelsDir, "ggml-tiny.en.bin");

        if (!System.IO.File.Exists(modelPath) || new System.IO.FileInfo(modelPath).Length < 10000000)
        {
            string tmpPath = modelPath + ".tmp";
            using (var stream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.TinyEn))
            using (var fs = new System.IO.FileStream(tmpPath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            {
                await stream.CopyToAsync(fs);
            }
            if (System.IO.File.Exists(modelPath)) System.IO.File.Delete(modelPath);
            System.IO.File.Move(tmpPath, modelPath);
        }

        Assert.True(System.IO.File.Exists(modelPath));
        Assert.True(new System.IO.FileInfo(modelPath).Length > 50000000);

        // Verify factory loads the model
        using var factory = WhisperFactory.FromPath(modelPath);
        Assert.NotNull(factory);
    }

    [Fact]
    public async System.Threading.Tasks.Task WhisperDownloader_DownloadsBaseEnModelSuccessfully()
    {
        string modelsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ORBIT", "models");
        System.IO.Directory.CreateDirectory(modelsDir);
        string modelPath = System.IO.Path.Combine(modelsDir, "ggml-base.en.bin");

        if (!System.IO.File.Exists(modelPath) || new System.IO.FileInfo(modelPath).Length < 100000000)
        {
            string tmpPath = modelPath + ".tmp";
            using (var stream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.BaseEn))
            using (var fs = new System.IO.FileStream(tmpPath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            {
                await stream.CopyToAsync(fs);
            }
            if (System.IO.File.Exists(modelPath)) System.IO.File.Delete(modelPath);
            System.IO.File.Move(tmpPath, modelPath);
        }

        Assert.True(System.IO.File.Exists(modelPath));
        Assert.True(new System.IO.FileInfo(modelPath).Length > 100000000);

        // Verify factory loads the model
        using var factory = WhisperFactory.FromPath(modelPath);
        Assert.NotNull(factory);
    }

    [Fact]
    public async System.Threading.Tasks.Task WhisperProcessor_ProcessesSilentAudioWithoutError()
    {
        string modelPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ORBIT", "models", "ggml-tiny.en.bin");

        using var factory = WhisperFactory.FromPath(modelPath);
        using var processor = factory.CreateBuilder()
            .WithLanguage("en")
            .WithSingleSegment()
            .WithNoContext()
            .WithThreads(2)
            .Build();

        float[] samples = new float[16000];
        var text = new System.Text.StringBuilder();
        await foreach (var segment in processor.ProcessAsync(samples))
        {
            text.Append(segment.Text);
        }

        Assert.NotNull(text.ToString());
    }

    [Fact]
    public async System.Threading.Tasks.Task Whisper_Benchmark_TinyVsBase_SixPhrases()
    {
        string modelsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ORBIT", "models");
        string tinyPath = System.IO.Path.Combine(modelsDir, "ggml-tiny.en.bin");
        string basePath = System.IO.Path.Combine(modelsDir, "ggml-base.en.bin");
        string audioDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ORBIT", "test_audio");

        Assert.True(System.IO.File.Exists(tinyPath), "Tiny model must exist");
        Assert.True(System.IO.File.Exists(basePath), "Base model must exist");

        var phrases = new[]
        {
            ("phrase1.wav", "Open Android Studio."),
            ("phrase2.wav", "Search for all Android Studio files."),
            ("phrase3.wav", "Open Google Chrome."),
            ("phrase4.wav", "Set volume to fifty percent."),
            ("phrase5.wav", "Take a screenshot."),
            ("phrase6.wav", "Hello Orbit.")
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== WHISPER BENCHMARK: TINY.EN vs BASE.EN ===");

        // --- Benchmark Tiny.en ---
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memBeforeTiny = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        var swWarmupTiny = System.Diagnostics.Stopwatch.StartNew();
        using var tinyFactory = WhisperFactory.FromPath(tinyPath);
        using var tinyProcessor = tinyFactory.CreateBuilder()
            .WithLanguage("en")
            .WithSingleSegment()
            .WithNoContext()
            .WithThreads(2)
            .Build();
        swWarmupTiny.Stop();
        long memAfterTiny = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        long tinyRamDeltaMb = (memAfterTiny - memBeforeTiny) / (1024 * 1024);

        sb.AppendLine($"[Tiny.en] Warm-up: {swWarmupTiny.ElapsedMilliseconds} ms | RAM delta: ~{tinyRamDeltaMb} MB");

        var tinyResults = new System.Collections.Generic.List<(string phrase, string recognized, long latencyMs)>();
        foreach (var (file, phrase) in phrases)
        {
            string wavPath = System.IO.Path.Combine(audioDir, file);
            float[] samples = LoadWavSamples(wavPath);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = new System.Text.StringBuilder();
            await foreach (var seg in tinyProcessor.ProcessAsync(samples))
            {
                text.Append(seg.Text).Append(' ');
            }
            sw.Stop();
            string cleaned = WhisperTranscriptionService.CleanRecognizedText(text.ToString());
            tinyResults.Add((phrase, cleaned, sw.ElapsedMilliseconds));
            sb.AppendLine($"  Tiny -> \"{phrase}\" => \"{cleaned}\" in {sw.ElapsedMilliseconds}ms");
        }

        // Dispose Tiny before loading Base to keep single-model memory measurement
        tinyProcessor.Dispose();
        tinyFactory.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // --- Benchmark Base.en ---
        long memBeforeBase = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        var swWarmupBase = System.Diagnostics.Stopwatch.StartNew();
        using var baseFactory = WhisperFactory.FromPath(basePath);
        using var baseProcessor = baseFactory.CreateBuilder()
            .WithLanguage("en")
            .WithSingleSegment()
            .WithNoContext()
            .WithThreads(2)
            .Build();
        swWarmupBase.Stop();
        long memAfterBase = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        long baseRamDeltaMb = (memAfterBase - memBeforeBase) / (1024 * 1024);

        sb.AppendLine($"[Base.en] Warm-up: {swWarmupBase.ElapsedMilliseconds} ms | RAM delta: ~{baseRamDeltaMb} MB");

        var baseResults = new System.Collections.Generic.List<(string phrase, string recognized, long latencyMs)>();
        foreach (var (file, phrase) in phrases)
        {
            string wavPath = System.IO.Path.Combine(audioDir, file);
            float[] samples = LoadWavSamples(wavPath);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = new System.Text.StringBuilder();
            await foreach (var seg in baseProcessor.ProcessAsync(samples))
            {
                text.Append(seg.Text).Append(' ');
            }
            sw.Stop();
            string cleaned = WhisperTranscriptionService.CleanRecognizedText(text.ToString());
            baseResults.Add((phrase, cleaned, sw.ElapsedMilliseconds));
            sb.AppendLine($"  Base -> \"{phrase}\" => \"{cleaned}\" in {sw.ElapsedMilliseconds}ms");
        }

        string reportPath = System.IO.Path.Combine(modelsDir, "benchmark_results.txt");
        System.IO.File.WriteAllText(reportPath, sb.ToString());

        Assert.NotEmpty(baseResults);
    }

    [Fact]
    public async System.Threading.Tasks.Task Whisper_Benchmark_BaseVsSmall_SixEvalPhrases()
    {
        string modelsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ORBIT", "models");
        string basePath = System.IO.Path.Combine(modelsDir, "ggml-base.en.bin");
        string smallPath = System.IO.Path.Combine(modelsDir, "ggml-small.en.bin");
        string audioDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ORBIT", "test_audio_eval");

        Assert.True(System.IO.File.Exists(basePath), "Base model must exist");
        Assert.True(System.IO.File.Exists(smallPath), "Small model must exist");

        var phrases = new[]
        {
            ("phrase1.wav", "Do I have enough memory for Android Studio so I can do my development work?"),
            ("phrase2.wav", "Open Android Studio."),
            ("phrase3.wav", "Operate Studio."),
            ("phrase4.wav", "Check my laptop RAM."),
            ("phrase5.wav", "Open File Explorer and search for college files on my desktop."),
            ("phrase6.wav", "Search Myntra for oversized hoodies.")
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== WHISPER BENCHMARK: BASE.EN vs SMALL.EN ===");

        // --- Benchmark Base.en ---
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memBeforeBase = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        var swWarmupBase = System.Diagnostics.Stopwatch.StartNew();
        using var baseFactory = WhisperFactory.FromPath(basePath);
        using var baseProcessor = baseFactory.CreateBuilder()
            .WithLanguage("en")
            .WithSingleSegment()
            .WithNoContext()
            .WithThreads(2)
            .Build();
        swWarmupBase.Stop();
        long memAfterBase = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        long baseRamDeltaMb = (memAfterBase - memBeforeBase) / (1024 * 1024);

        sb.AppendLine($"[Base.en] Warm-up: {swWarmupBase.ElapsedMilliseconds} ms | RAM delta: ~{baseRamDeltaMb} MB");

        var baseResults = new System.Collections.Generic.List<(string phrase, string recognized, long latencyMs)>();
        foreach (var (file, phrase) in phrases)
        {
            string wavPath = System.IO.Path.Combine(audioDir, file);
            float[] samples = LoadWavSamples(wavPath);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = new System.Text.StringBuilder();
            await foreach (var seg in baseProcessor.ProcessAsync(samples))
            {
                text.Append(seg.Text).Append(' ');
            }
            sw.Stop();
            string cleaned = WhisperTranscriptionService.CleanRecognizedText(text.ToString());
            baseResults.Add((phrase, cleaned, sw.ElapsedMilliseconds));
            sb.AppendLine($"  Base -> \"{phrase}\" => \"{cleaned}\" in {sw.ElapsedMilliseconds}ms");
        }

        baseProcessor.Dispose();
        baseFactory.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // --- Benchmark Small.en ---
        long memBeforeSmall = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        var swWarmupSmall = System.Diagnostics.Stopwatch.StartNew();
        using var smallFactory = WhisperFactory.FromPath(smallPath);
        using var smallProcessor = smallFactory.CreateBuilder()
            .WithLanguage("en")
            .WithSingleSegment()
            .WithNoContext()
            .WithThreads(2)
            .Build();
        swWarmupSmall.Stop();
        long memAfterSmall = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        long smallRamDeltaMb = (memAfterSmall - memBeforeSmall) / (1024 * 1024);

        sb.AppendLine($"[Small.en] Warm-up: {swWarmupSmall.ElapsedMilliseconds} ms | RAM delta: ~{smallRamDeltaMb} MB");

        var smallResults = new System.Collections.Generic.List<(string phrase, string recognized, long latencyMs)>();
        foreach (var (file, phrase) in phrases)
        {
            string wavPath = System.IO.Path.Combine(audioDir, file);
            float[] samples = LoadWavSamples(wavPath);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = new System.Text.StringBuilder();
            await foreach (var seg in smallProcessor.ProcessAsync(samples))
            {
                text.Append(seg.Text).Append(' ');
            }
            sw.Stop();
            string cleaned = WhisperTranscriptionService.CleanRecognizedText(text.ToString());
            smallResults.Add((phrase, cleaned, sw.ElapsedMilliseconds));
            sb.AppendLine($"  Small -> \"{phrase}\" => \"{cleaned}\" in {sw.ElapsedMilliseconds}ms");
        }

        smallProcessor.Dispose();
        smallFactory.Dispose();

        string evalReportPath = System.IO.Path.Combine(modelsDir, "benchmark_base_vs_small.txt");
        System.IO.File.WriteAllText(evalReportPath, sb.ToString());

        Assert.NotEmpty(smallResults);
    }

    [Fact]
    public async System.Threading.Tasks.Task Whisper_Benchmark_BiasedVocabulary_EightEvalPhrases()
    {
        string modelsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ORBIT", "models");
        string smallPath = System.IO.Path.Combine(modelsDir, "ggml-small.en.bin");
        string audioDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ORBIT", "test_audio_eval_biased");

        Assert.True(System.IO.File.Exists(smallPath), "Small model must exist");

        var phrases = new[]
        {
            ("eval1.wav", "Open Android Studio."),
            ("eval2.wav", "Operate Studio."),
            ("eval3.wav", "Open Antigravity."),
            ("eval4.wav", "Search Myntra for oversized hoodies."),
            ("eval5.wav", "Check my laptop RAM."),
            ("eval6.wav", "Open File Explorer and search my desktop."),
            ("eval7.wav", "Do I have enough memory for Android Studio?"),
            ("eval8.wav", "Explain Jetpack Compose.")
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== WHISPER VOCABULARY BIASING BENCHMARK: SMALL.EN (NO CONTEXT vs WITH PROMPT) ===");

        // --- Benchmark 1: Small.en with No Context (Baseline) ---
        using var smallFactory = WhisperFactory.FromPath(smallPath);
        using var unpromptedProcessor = smallFactory.CreateBuilder()
            .WithLanguage("en")
            .WithSingleSegment()
            .WithNoContext()
            .WithThreads(2)
            .Build();

        sb.AppendLine("[Small.en - Without Prompt Biasing]");
        var unpromptedResults = new System.Collections.Generic.List<(string phrase, string recognized, long latencyMs)>();
        foreach (var (file, phrase) in phrases)
        {
            string wavPath = System.IO.Path.Combine(audioDir, file);
            float[] samples = LoadWavSamples(wavPath);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = new System.Text.StringBuilder();
            await foreach (var seg in unpromptedProcessor.ProcessAsync(samples))
            {
                text.Append(seg.Text).Append(' ');
            }
            sw.Stop();
            string cleaned = WhisperTranscriptionService.CleanRecognizedText(text.ToString());
            unpromptedResults.Add((phrase, cleaned, sw.ElapsedMilliseconds));
            sb.AppendLine($"  Unbiased -> \"{phrase}\" => \"{cleaned}\" in {sw.ElapsedMilliseconds}ms");
        }

        unpromptedProcessor.Dispose();

        // --- Benchmark 2: Small.en with Technical Vocabulary Prompt Biasing ---
        using var biasedProcessor = smallFactory.CreateBuilder()
            .WithLanguage("en")
            .WithSingleSegment()
            .WithPrompt(WhisperTranscriptionService.VocabularyPrompt)
            .WithThreads(2)
            .Build();

        sb.AppendLine("\n[Small.en - With Technical Vocabulary Prompt Biasing]");
        var biasedResults = new System.Collections.Generic.List<(string phrase, string recognized, long latencyMs)>();
        foreach (var (file, phrase) in phrases)
        {
            string wavPath = System.IO.Path.Combine(audioDir, file);
            float[] samples = LoadWavSamples(wavPath);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = new System.Text.StringBuilder();
            await foreach (var seg in biasedProcessor.ProcessAsync(samples))
            {
                text.Append(seg.Text).Append(' ');
            }
            sw.Stop();
            string cleaned = WhisperTranscriptionService.CleanRecognizedText(text.ToString());
            biasedResults.Add((phrase, cleaned, sw.ElapsedMilliseconds));
            sb.AppendLine($"  Biased   -> \"{phrase}\" => \"{cleaned}\" in {sw.ElapsedMilliseconds}ms");
        }

        biasedProcessor.Dispose();

        string biasReportPath = System.IO.Path.Combine(modelsDir, "benchmark_biased_vocabulary.txt");
        System.IO.File.WriteAllText(biasReportPath, sb.ToString());

        Assert.NotEmpty(biasedResults);
    }

    private static float[] LoadWavSamples(string wavPath)
    {
        byte[] bytes = System.IO.File.ReadAllBytes(wavPath);
        int dataIdx = 44;
        for (int i = 0; i < bytes.Length - 4; i++)
        {
            if (bytes[i] == 'd' && bytes[i + 1] == 'a' && bytes[i + 2] == 't' && bytes[i + 3] == 'a')
            {
                dataIdx = i + 8;
                break;
            }
        }
        int pcmBytes = bytes.Length - dataIdx;
        int sampleCount = pcmBytes / 2;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short pcm = (short)(bytes[dataIdx + i * 2] | (bytes[dataIdx + i * 2 + 1] << 8));
            samples[i] = pcm / 32768.0f;
        }
        return samples;
    }

    [Fact]
    public void VoiceSession_StartsInIdleState()
    {
        var fakeCapture = new FakeAudioCaptureService();
        using var session = new VoiceSessionManager(fakeCapture);

        Assert.Equal(VoiceSessionState.Idle, session.CurrentState);
        Assert.False(session.IsActive);
    }

    [Fact]
    public void VoiceSession_TransitionsToListening_OnStartSession()
    {
        var fakeCapture = new FakeAudioCaptureService();
        using var session = new VoiceSessionManager(fakeCapture);

        bool started = session.StartSession();

        Assert.True(started);
        Assert.Equal(VoiceSessionState.Listening, session.CurrentState);
        Assert.True(session.IsActive);
        Assert.True(fakeCapture.IsCapturing);
    }

    [Fact]
    public void VoiceSession_HandlesCaptureErrorGracefully()
    {
        var fakeCapture = new FakeAudioCaptureService { StartCaptureResult = false };
        using var session = new VoiceSessionManager(fakeCapture);

        bool started = session.StartSession();

        Assert.False(started);
        Assert.Equal(VoiceSessionState.Error, session.CurrentState);
        Assert.False(session.IsActive);
    }

    [Fact]
    public void VoiceSession_CancelPurgesBuffersAndReturnsToIdle()
    {
        var fakeCapture = new FakeAudioCaptureService();
        using var session = new VoiceSessionManager(fakeCapture);

        session.StartSession();
        Assert.Equal(VoiceSessionState.Listening, session.CurrentState);

        session.CancelSession();
        Assert.Equal(VoiceSessionState.Idle, session.CurrentState);
        Assert.False(session.IsActive);
        Assert.False(fakeCapture.IsCapturing);
    }

    [Fact]
    public void Vad_DetectsSpeechOnset_AndCompletesAfterTrailingSilence()
    {
        var vad = new VoiceActivityDetector
        {
            InitialSilenceTimeoutSeconds = 1.0,
            TrailingSilenceTimeoutSeconds = 0.05,
            MaxDurationSeconds = 2.0
        };

        bool speechStartedFired = false;
        string? completionReason = null;

        vad.SpeechStarted += (s, e) => speechStartedFired = true;
        vad.UtteranceCompleted += (s, reason) => completionReason = reason;

        vad.Start();

        // 1. Calibration phase (200ms) with low noise floor (0.01)
        Thread.Sleep(210);
        vad.ProcessIntensity(0.01f);

        Assert.True(vad.CurrentNoiseFloor <= 0.05f);

        // 2. Speech onset (high intensity)
        vad.ProcessIntensity(0.50f);
        Assert.True(speechStartedFired);
        Assert.True(vad.HasSpeechStarted);

        // 3. Trailing silence below threshold: first low frame starts silence stopwatch
        vad.ProcessIntensity(0.01f);

        // Sleep to exceed 0.05s TrailingSilenceTimeoutSeconds, then feed next low frame to trigger completion
        Thread.Sleep(70);
        vad.ProcessIntensity(0.01f);

        Assert.Equal("SilenceDetected", completionReason);
    }

    [Fact]
    public void Vad_TimesOutOnInitialSilence_WhenNoSpeechOccurs()
    {
        var vad = new VoiceActivityDetector
        {
            InitialSilenceTimeoutSeconds = 0.05,
            TrailingSilenceTimeoutSeconds = 1.0,
            MaxDurationSeconds = 2.0
        };

        string? completionReason = null;
        vad.UtteranceCompleted += (s, reason) => completionReason = reason;

        vad.Start();

        // Calibration phase
        Thread.Sleep(210);
        vad.ProcessIntensity(0.01f);

        // Initial silence timeout: sleep past 0.05s, then feed next sample
        Thread.Sleep(70);
        vad.ProcessIntensity(0.01f);

        Assert.Equal("InitialSilenceTimeout", completionReason);
        Assert.False(vad.HasSpeechStarted);
    }

    [Fact]
    public void Vad_CapsRecordingAtMaxDuration()
    {
        var vad = new VoiceActivityDetector
        {
            InitialSilenceTimeoutSeconds = 5.0,
            TrailingSilenceTimeoutSeconds = 5.0,
            MaxDurationSeconds = 0.25
        };

        string? completionReason = null;
        vad.UtteranceCompleted += (s, reason) => completionReason = reason;

        vad.Start();

        // Feed sample to calibrate
        Thread.Sleep(210);
        vad.ProcessIntensity(0.01f);

        // Wait past max duration (0.25s total elapsed)
        Thread.Sleep(60);
        vad.ProcessIntensity(0.30f);

        Assert.Equal("MaxDurationReached", completionReason);
    }

    [Fact]
    public void WhisperModelManager_PointsToPersistentAppDataAndDetectsModel()
    {
        var manager = new WhisperModelManager();
        Assert.Contains(System.IO.Path.Combine("ORBIT", "models", "ggml-small.en.bin"), manager.ModelPath);
        Assert.Contains(System.IO.Path.Combine("ORBIT", "models", "ggml-base.en.bin"), manager.FallbackModelPath);
        Assert.True(manager.IsModelAvailable);
        Assert.True(manager.IsFallbackAvailable);
    }

    [Fact]
    public void WhisperTranscriptionService_ConvertPcm16ToFloat_NormalizesCorrectly()
    {
        // 0 -> 0.0f, 32767 -> ~1.0f, -32768 -> -1.0f
        byte[] pcm = new byte[6];
        pcm[0] = 0; pcm[1] = 0;                     // 0
        pcm[2] = 0xFF; pcm[3] = 0x7F;               // 32767
        pcm[4] = 0x00; pcm[5] = 0x80;               // -32768

        var floats = WhisperTranscriptionService.ConvertPcm16ToFloat(pcm);

        Assert.Equal(3, floats.Length);
        Assert.Equal(0f, floats[0], 0.001f);
        Assert.True(floats[1] > 0.99f && floats[1] <= 1.0f);
        Assert.Equal(-1.0f, floats[2], 0.001f);
    }

    [Fact]
    public void WhisperTranscriptionService_CleanRecognizedText_SuppressesHallucinationsAndTrims()
    {
        Assert.Equal("", WhisperTranscriptionService.CleanRecognizedText("[BLANK_AUDIO]"));
        Assert.Equal("", WhisperTranscriptionService.CleanRecognizedText("you"));
        Assert.Equal("", WhisperTranscriptionService.CleanRecognizedText("Thank you."));
        Assert.Equal("", WhisperTranscriptionService.CleanRecognizedText("(silence)"));
        Assert.Equal("open settings", WhisperTranscriptionService.CleanRecognizedText(" \"open settings.\" "));
        Assert.Equal("take a note", WhisperTranscriptionService.CleanRecognizedText("take a note!"));
    }

    private class FakeWhisperTranscriptionService : IWhisperTranscriptionService
    {
        public bool IsModelLoaded { get; set; } = true;
        public string LoadedModelName => "fake-small.en.bin";
        public string ResultToReturn { get; set; } = "open settings";
        public int DelayMs { get; set; } = 50;

        public System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;

        public async System.Threading.Tasks.Task<string> TranscribePcm16Async(byte[] pcmData, CancellationToken cancellationToken = default)
        {
            await System.Threading.Tasks.Task.Delay(DelayMs, cancellationToken);
            return ResultToReturn;
        }

        public void Dispose() { }
    }

    [Fact]
    public async System.Threading.Tasks.Task VoiceSessionManager_WithTranscription_TransitionsToProcessingAndEmitsTranscription()
    {
        var fakeCapture = new FakeAudioCaptureService
        {
            FakeData = new byte[16000] // 0.5s of audio
        };
        var fakeWhisper = new FakeWhisperTranscriptionService
        {
            ResultToReturn = "hello orbit",
            DelayMs = 20
        };

        using var session = new VoiceSessionManager(fakeCapture, fakeWhisper);

        var states = new System.Collections.Generic.List<VoiceSessionState>();
        session.StateChanged += (s, state) => states.Add(state);

        string? receivedText = null;
        session.TranscriptionReceived += (s, text) => receivedText = text;

        session.StartSession();
        session.StopSession();

        // Wait for async transcription
        await System.Threading.Tasks.Task.Delay(100);

        Assert.Equal("hello orbit", receivedText);
        Assert.Contains(VoiceSessionState.Processing, states);
        Assert.Contains(VoiceSessionState.Completed, states);
        Assert.Equal(VoiceSessionState.Idle, session.CurrentState);
    }

    [Fact]
    public async System.Threading.Tasks.Task VoiceSessionManager_CancelDuringProcessing_DiscardsTranscription()
    {
        var fakeCapture = new FakeAudioCaptureService
        {
            FakeData = new byte[16000]
        };
        var fakeWhisper = new FakeWhisperTranscriptionService
        {
            ResultToReturn = "stale command",
            DelayMs = 80
        };

        using var session = new VoiceSessionManager(fakeCapture, fakeWhisper);

        string? receivedText = null;
        session.TranscriptionReceived += (s, text) => receivedText = text;

        session.StartSession();
        session.StopSession(); // enters Processing

        // Immediately cancel before transcription completes
        Thread.Sleep(15);
        session.CancelSession();

        // Wait past transcription delay
        await System.Threading.Tasks.Task.Delay(150);

        Assert.Null(receivedText); // Stale transcription MUST be discarded
        Assert.Equal(VoiceSessionState.Idle, session.CurrentState);
    }

    [Fact]
    public async System.Threading.Tasks.Task RealWhisperService_InitializesAndReusesModel()
    {
        var modelManager = new WhisperModelManager();
        using var service = new WhisperTranscriptionService(modelManager);

        Assert.False(service.IsModelLoaded);

        await service.InitializeAsync();
        Assert.True(service.IsModelLoaded);
        Assert.Equal("ggml-small.en.bin", service.LoadedModelName);

        // Transcribe silent audio
        byte[] pcm = new byte[16000]; // 0.5s of silence
        var result = await service.TranscribePcm16Async(pcm);

        Assert.NotNull(result);
        Assert.True(service.IsModelLoaded); // Reused
    }
}
