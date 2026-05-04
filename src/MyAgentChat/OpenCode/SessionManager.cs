using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MyAgentChat.OpenCode;

/// <summary>
/// Maps nostr pubkeys to OpenCode session IDs.
/// Enables continuous conversations per sender.
/// </summary>
public class SessionManager
{
    private readonly OpenCodeClient _client;
    private readonly ILogger<SessionManager> _logger;
    private readonly ConcurrentDictionary<string, string> _sessions = new();

    public SessionManager(OpenCodeClient client, ILogger<SessionManager> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Gets the existing session ID for a pubkey, or creates a new one.
    /// </summary>
    public async Task<string> GetOrCreateSessionAsync(string pubKeyHex, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(pubKeyHex, out var sessionId))
        {
            _logger.LogDebug("Found existing session {SessionId} for {PubKey}", sessionId, pubKeyHex[..8]);
            return sessionId;
        }

        sessionId = await _client.CreateSessionAsync($"nostr-{pubKeyHex[..8]}", ct);
        _sessions[pubKeyHex] = sessionId;
        _logger.LogInformation("Created new session {SessionId} for {PubKey}", sessionId, pubKeyHex[..8]);
        return sessionId;
    }

    /// <summary>
    /// Forces creation of a new session for a pubkey (e.g. /new command).
    /// </summary>
    public async Task<string> CreateNewSessionAsync(string pubKeyHex, CancellationToken ct = default)
    {
        var sessionId = await _client.CreateSessionAsync($"nostr-{pubKeyHex[..8]}", ct);
        _sessions[pubKeyHex] = sessionId;
        _logger.LogInformation("Force-created new session {SessionId} for {PubKey}", sessionId, pubKeyHex[..8]);
        return sessionId;
    }

    /// <summary>
    /// Gets the current session ID for a pubkey, or null if none exists.
    /// </summary>
    public string? GetSession(string pubKeyHex)
    {
        _sessions.TryGetValue(pubKeyHex, out var sessionId);
        return sessionId;
    }

    /// <summary>
    /// Returns the number of active sessions.
    /// </summary>
    public int SessionCount => _sessions.Count;
}
