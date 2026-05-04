using Microsoft.Extensions.Logging;
using NBitcoin.Secp256k1;
using NNostr.Client;
using MyAgentChat.Config;
using MyAgentChat.Nostr;
using MyAgentChat.OpenCode;

namespace MyAgentChat.Services;

/// <summary>
/// Core bridge: receives decrypted nostr DM → routes to OpenCode → sends reply via NIP-17.
/// </summary>
public class MessageProcessor
{
    private readonly AppSettings _settings;
    private readonly Nip17Decryptor _decryptor;
    private readonly EventPublisher _publisher;
    private readonly OpenCodeClient _openCode;
    private readonly SessionManager _sessions;
    private readonly ILogger<MessageProcessor> _logger;
    private readonly ECPrivKey _agentPrivKey;
    private readonly string _authorizedPubKeyHex;
    private readonly DateTimeOffset _startTime;

    public MessageProcessor(
        AppSettings settings,
        Nip17Decryptor decryptor,
        EventPublisher publisher,
        OpenCodeClient openCode,
        SessionManager sessions,
        ILogger<MessageProcessor> logger)
    {
        _settings = settings;
        _decryptor = decryptor;
        _publisher = publisher;
        _openCode = openCode;
        _sessions = sessions;
        _logger = logger;
        _agentPrivKey = KeyHelper.ParsePrivateKey(settings.Nostr.AgentPrivateKey);
        _authorizedPubKeyHex = KeyHelper.ParsePubKeyHex(settings.Nostr.AuthorizedNpub);
        _startTime = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Handles an incoming gift wrap event from the relay.
    /// </summary>
    public async Task HandleGiftWrapAsync(NostrEvent giftWrap, INostrClient relayClient, CancellationToken ct)
    {
        // Decrypt NIP-17
        var rumor = _decryptor.Decrypt(giftWrap, _agentPrivKey);
        if (rumor == null)
        {
            _logger.LogWarning("Failed to decrypt gift wrap {Id}", giftWrap.Id);
            return;
        }

        // Get sender pubkey
        var senderPubKey = Nip17Decryptor.GetSenderPubKey(giftWrap, _agentPrivKey);
        if (senderPubKey == null)
        {
            _logger.LogWarning("Could not determine sender pubkey for {Id}", giftWrap.Id);
            return;
        }

        // Authorize
        if (!senderPubKey.Equals(_authorizedPubKeyHex, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Unauthorized sender: {PubKey}", senderPubKey[..8]);
            return;
        }

        // Skip old messages
        if (rumor.CreatedAt.HasValue)
        {
            var age = DateTimeOffset.UtcNow - rumor.CreatedAt.Value;
            if (age.TotalMinutes > _settings.General.SkipOlderThanMinutes)
            {
                _logger.LogInformation("Skipping old message ({Age} min): {Content}",
                    (int)age.TotalMinutes, rumor.Content?[..Math.Min(50, rumor.Content?.Length ?? 0)]);
                return;
            }
        }

        var messageText = rumor.Content?.Trim();
        if (string.IsNullOrEmpty(messageText))
        {
            _logger.LogWarning("Empty message from {PubKey}", senderPubKey[..8]);
            return;
        }

        _logger.LogInformation("Processing DM from {PubKey}: {Text}",
            senderPubKey[..8], messageText.Length > 100 ? messageText[..100] + "..." : messageText);

        // Check for special commands
        var commandResponse = await HandleSpecialCommandAsync(messageText, senderPubKey, ct);
        if (commandResponse != null)
        {
            await _publisher.SendDmReplyAsync(senderPubKey, commandResponse, _agentPrivKey, relayClient, ct);
            return;
        }

        // Route to OpenCode
        try
        {
            var sessionId = await _sessions.GetOrCreateSessionAsync(senderPubKey, ct);
            var response = await _openCode.SendMessageAsync(sessionId, messageText, ct);

            if (string.IsNullOrEmpty(response))
            {
                response = "(empty response from OpenCode)";
            }

            // Truncate if needed
            if (response.Length > _settings.General.MaxResponseLength)
            {
                response = response[..(_settings.General.MaxResponseLength - 15)] + "\n...(truncated)";
            }

            await _publisher.SendDmReplyAsync(senderPubKey, response, _agentPrivKey, relayClient, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {PubKey}", senderPubKey[..8]);
            var errorMsg = $"Error: {ex.Message}";
            await _publisher.SendDmReplyAsync(senderPubKey, errorMsg, _agentPrivKey, relayClient, ct);
        }
    }

    private async Task<string?> HandleSpecialCommandAsync(string text, string senderPubKey, CancellationToken ct)
    {
        if (!text.StartsWith('/'))
            return null;

        var command = text.ToLowerInvariant().Trim();

        return command switch
        {
            "/status" => GetStatusMessage(),
            "/sessions" => $"Active sessions: {_sessions.SessionCount}",
            "/new" => await HandleNewSessionAsync(senderPubKey, ct),
            "/abort" => await HandleAbortAsync(senderPubKey, ct),
            _ => null // Not a special command, route to OpenCode
        };
    }

    private string GetStatusMessage()
    {
        var uptime = DateTimeOffset.UtcNow - _startTime;
        return $"Agent status:\n" +
               $"- Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m\n" +
               $"- Active sessions: {_sessions.SessionCount}\n" +
               $"- OpenCode: {_settings.OpenCode.BaseUrl}";
    }

    private async Task<string> HandleNewSessionAsync(string senderPubKey, CancellationToken ct)
    {
        var sessionId = await _sessions.CreateNewSessionAsync(senderPubKey, ct);
        return $"New session created: {sessionId}";
    }

    private async Task<string> HandleAbortAsync(string senderPubKey, CancellationToken ct)
    {
        var sessionId = _sessions.GetSession(senderPubKey);
        if (sessionId == null)
            return "No active session to abort.";

        await _openCode.AbortAsync(sessionId, ct);
        return "Generation aborted.";
    }
}
