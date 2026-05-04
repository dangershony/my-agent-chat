using NBitcoin.Secp256k1;

namespace MyAgentChat.Nostr;

/// <summary>
/// Helpers for parsing nostr keys (nsec/npub/hex).
/// </summary>
public static class KeyHelper
{
    /// <summary>
    /// Parses an nsec (bech32) or hex private key string into an ECPrivKey.
    /// </summary>
    public static ECPrivKey ParsePrivateKey(string key)
    {
        if (key.StartsWith("nsec1"))
        {
            var bytes = Bech32.Decode(key, out _);
            if (bytes == null || bytes.Length < 32)
                throw new ArgumentException("Invalid nsec key");
            return ECPrivKey.Create(bytes[..32]);
        }

        // Assume hex
        return ECPrivKey.Create(Convert.FromHexString(key));
    }

    /// <summary>
    /// Parses an npub (bech32) or hex public key string into a hex pubkey.
    /// </summary>
    public static string ParsePubKeyHex(string key)
    {
        if (key.StartsWith("npub1"))
        {
            var bytes = Bech32.Decode(key, out _);
            if (bytes == null || bytes.Length < 32)
                throw new ArgumentException("Invalid npub key");
            return Convert.ToHexString(bytes[..32]).ToLowerInvariant();
        }

        // Assume hex
        return key.ToLowerInvariant();
    }
}
