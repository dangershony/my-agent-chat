using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MyAgentChat.Config;

namespace MyAgentChat.OpenCode;

/// <summary>
/// HTTP client for the OpenCode REST API (opencode serve).
/// </summary>
public class OpenCodeClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenCodeClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenCodeClient(AppSettings settings, ILogger<OpenCodeClient> logger)
    {
        _logger = logger;
        _http = new HttpClient
        {
            BaseAddress = new Uri(settings.OpenCode.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(10) // OpenCode calls can take a while
        };

        if (!string.IsNullOrEmpty(settings.OpenCode.Password))
        {
            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{settings.OpenCode.Password}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        }
    }

    /// <summary>
    /// Creates a new OpenCode session. Returns the session ID.
    /// </summary>
    public async Task<string> CreateSessionAsync(string? title = null, CancellationToken ct = default)
    {
        var body = new { title };
        var response = await _http.PostAsJsonAsync("session", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var sessionId = json.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("No session ID in response");

        _logger.LogInformation("Created OpenCode session: {SessionId}", sessionId);
        return sessionId;
    }

    /// <summary>
    /// Sends a message to an OpenCode session (synchronous — blocks until response).
    /// Returns the assistant's response text.
    /// </summary>
    public async Task<string> SendMessageAsync(string sessionId, string text, CancellationToken ct = default)
    {
        var body = new
        {
            parts = new[]
            {
                new { type = "text", text }
            }
        };

        _logger.LogInformation("Sending message to session {SessionId}: {Text}",
            sessionId, text.Length > 100 ? text[..100] + "..." : text);

        var response = await _http.PostAsJsonAsync($"session/{sessionId}/message", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Extract text from response parts
        var sb = new StringBuilder();
        if (json.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "text"
                    && part.TryGetProperty("text", out var partText))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(partText.GetString());
                }
            }
        }

        var result = sb.ToString();
        _logger.LogInformation("Got response from session {SessionId}: {Length} chars", sessionId, result.Length);
        return result;
    }

    /// <summary>
    /// Aborts the current generation in a session.
    /// </summary>
    public async Task AbortAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"session/{sessionId}/abort", null, ct);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Aborted generation in session {SessionId}", sessionId);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
