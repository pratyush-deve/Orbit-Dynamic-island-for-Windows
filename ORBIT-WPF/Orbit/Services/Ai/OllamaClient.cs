using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;

namespace Orbit.Services.Ai;

public record AiChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public interface IOllamaClient : IDisposable
{
    bool IsServiceAvailable { get; }
    Task<bool> CheckServiceHealthAsync(CancellationToken cancellationToken = default);
    Task StreamChatAsync(
        IEnumerable<AiChatMessage> messages,
        Action<string> onChunkReceived,
        string model = "qwen2.5:3b",
        CancellationToken cancellationToken = default);
    Task<bool> UnloadModelAsync(string model = "qwen2.5:3b", CancellationToken cancellationToken = default);
}

public sealed class OllamaClient : IOllamaClient
{
    private const string LogCategory = "OLLAMA_CLIENT";
    private const string DefaultEndpoint = "http://localhost:11434";
    private readonly HttpClient _httpClient;
    private bool _isServiceAvailable;
    private bool _disposed;

    public bool IsServiceAvailable => _isServiceAvailable;

    public OllamaClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(DefaultEndpoint),
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    public async Task<bool> CheckServiceHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2.5));

            var response = await _httpClient.GetAsync("/api/tags", cts.Token).ConfigureAwait(false);
            _isServiceAvailable = response.IsSuccessStatusCode;
            return _isServiceAvailable;
        }
        catch (Exception ex)
        {
            _isServiceAvailable = false;
            OrbitLogger.Debug(LogCategory, $"Ollama health check returned false: {ex.Message}");
            return false;
        }
    }

    public async Task StreamChatAsync(
        IEnumerable<AiChatMessage> messages,
        Action<string> onChunkReceived,
        string model = "qwen2.5:3b",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(onChunkReceived);

        if (_disposed) throw new ObjectDisposedException(nameof(OllamaClient));

        var requestPayload = new
        {
            model = model,
            messages = messages,
            stream = true,
            options = new
            {
                temperature = 0.4,
                num_predict = 150
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(requestPayload)
        };

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException($"Ollama returned status {(int)response.StatusCode}: {errorBody}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("message", out var msgElement) &&
                        msgElement.TryGetProperty("content", out var contentElement))
                    {
                        var chunk = contentElement.GetString();
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            onChunkReceived(chunk);
                        }
                    }

                    if (root.TryGetProperty("done", out var doneElement) && doneElement.GetBoolean())
                    {
                        break;
                    }
                }
                catch (JsonException ex)
                {
                    OrbitLogger.Warn(LogCategory, $"Malformed streaming JSON chunk: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            OrbitLogger.Info(LogCategory, "Ollama chat stream was cancelled by user.");
            throw;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Ollama streaming failed: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<bool> UnloadModelAsync(string model = "qwen2.5:3b", CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                model = model,
                keep_alive = 0
            };

            var response = await _httpClient.PostAsJsonAsync("/api/generate", payload, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                OrbitLogger.Info(LogCategory, $"Successfully unloaded model '{model}' from memory.");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Failed to unload model '{model}': {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}

