using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NBitcoin.Secp256k1;
using NNostr.Client;
using NNostr.Client.Protocols;

namespace MyAgentChat.Nostr;

/// <summary>
/// Publishes NIP-17 gift wrap DM replies back to the sender.
/// </summary>
public class EventPublisher
{
    private readonly ILogger<EventPublisher> _logger;

    public EventPublisher(ILogger<EventPublisher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sends a NIP-17 DM reply back to the sender.
    /// </summary>
    public async Task SendDmReplyAsync(
        string recipientPubKeyHex,
        string message,
        ECPrivKey senderPrivKey,
        INostrClient client,
        CancellationToken ct)
    {
        try
        {
            var recipientPubKey = NostrExtensions.ParsePubKey(recipientPubKeyHex);
            var senderPubKeyHex = Convert.ToHexString(senderPrivKey.CreateXOnlyPubKey().ToBytes()).ToLowerInvariant();

            // Layer 1: Rumor (kind 14) — the actual message
            // MUST have pubkey (sender identity) and computed id, but NO signature
            var rumor = new NostrEvent
            {
                Kind = 14,
                PublicKey = senderPubKeyHex,
                Content = message,
                CreatedAt = DateTimeOffset.UtcNow
            };
            rumor.SetTag("p", recipientPubKeyHex);

            // Compute the rumor's id (hash) but do NOT sign it
            rumor.Id = ComputeEventId(rumor);

            var rumorJson = JsonSerializer.Serialize(rumor);

            // Layer 2: Seal (kind 13) — encrypts the rumor, signed by sender
            var seal = new NostrEvent
            {
                Kind = 13,
                Content = NIP44.Encrypt(senderPrivKey, recipientPubKey, rumorJson),
                CreatedAt = RandomizeTimestamp()
            };
            await seal.ComputeIdAndSignAsync(senderPrivKey);
            var sealJson = JsonSerializer.Serialize(seal);

            // Layer 3: Gift Wrap (kind 1059) — encrypts the seal, signed by ephemeral key
            var ephemeralKey = ECPrivKey.Create(RandomNumberGenerator.GetBytes(32));
            var giftWrap = new NostrEvent
            {
                Kind = 1059,
                Content = NIP44.Encrypt(ephemeralKey, recipientPubKey, sealJson),
                CreatedAt = RandomizeTimestamp()
            };
            giftWrap.SetTag("p", recipientPubKeyHex);
            await giftWrap.ComputeIdAndSignAsync(ephemeralKey);

            await client.PublishEvent(giftWrap, ct);

            _logger.LogInformation("Sent DM reply to {Recipient}", recipientPubKeyHex[..8]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send DM reply");
        }
    }

    /// <summary>
    /// Computes the event id (SHA256 of serialized event) without signing.
    /// Per NIP-01: sha256(serialize([0, pubkey, created_at, kind, tags, content]))
    /// </summary>
    private static string ComputeEventId(NostrEvent evt)
    {
        var tags = evt.Tags?.Select(t =>
        {
            var arr = new List<string> { t.TagIdentifier };
            if (t.Data != null) arr.AddRange(t.Data);
            return arr;
        }).ToArray() ?? [];

        var serialized = JsonSerializer.Serialize(new object[]
        {
            0,
            evt.PublicKey ?? "",
            evt.CreatedAt?.ToUnixTimeSeconds() ?? 0,
            evt.Kind,
            tags,
            evt.Content ?? ""
        });

        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(serialized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static DateTimeOffset RandomizeTimestamp()
    {
        var random = new Random();
        var secondsOffset = random.Next(0, 172800); // 0 to 48 hours
        return DateTimeOffset.UtcNow.AddSeconds(-secondsOffset);
    }
}
