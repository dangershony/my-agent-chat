namespace MyAgentChat.Config;

public class AppSettings
{
    public NostrSettings Nostr { get; set; } = new();
    public OpenCodeSettings OpenCode { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
}

public class NostrSettings
{
    /// <summary>
    /// The agent's private key (nsec or hex). Used for decrypting DMs and sending replies.
    /// </summary>
    public string AgentPrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// The npub (or hex pubkey) authorized to chat with the agent.
    /// </summary>
    public string AuthorizedNpub { get; set; } = string.Empty;

    /// <summary>
    /// Relay WebSocket URLs.
    /// </summary>
    public string[] Relays { get; set; } = [];
}

public class OpenCodeSettings
{
    /// <summary>
    /// Base URL for the OpenCode HTTP API.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:4096";

    /// <summary>
    /// Optional HTTP Basic Auth password (set OPENCODE_SERVER_PASSWORD on the server side).
    /// </summary>
    public string? Password { get; set; }
}

public class GeneralSettings
{
    /// <summary>
    /// Maximum response length before truncation.
    /// </summary>
    public int MaxResponseLength { get; set; } = 4000;

    /// <summary>
    /// Skip messages older than this many minutes (prevents replaying old DMs on startup).
    /// </summary>
    public int SkipOlderThanMinutes { get; set; } = 5;
}
