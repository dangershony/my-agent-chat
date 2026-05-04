using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin.Secp256k1;
using NNostr.Client.Protocols;
using MyAgentChat.Config;
using MyAgentChat.Nostr;
using MyAgentChat.OpenCode;
using MyAgentChat.Services;

var builder = Host.CreateApplicationBuilder(args);

// Bind configuration
var settings = new AppSettings();
builder.Configuration.Bind(settings);
builder.Services.AddSingleton(settings);

// Register services
builder.Services.AddSingleton<Nip17Decryptor>();
builder.Services.AddSingleton<EventPublisher>();
builder.Services.AddSingleton<NostrRelayClient>();
builder.Services.AddSingleton<OpenCodeClient>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<MessageProcessor>();
builder.Services.AddHostedService<AgentHostedService>();

var host = builder.Build();
await host.RunAsync();

/// <summary>
/// Background service that connects to relays and processes incoming DMs.
/// </summary>
public class AgentHostedService : IHostedService
{
    private readonly NostrRelayClient _relayClient;
    private readonly MessageProcessor _processor;
    private readonly AppSettings _settings;
    private readonly ILogger<AgentHostedService> _logger;

    public AgentHostedService(
        NostrRelayClient relayClient,
        MessageProcessor processor,
        AppSettings settings,
        ILogger<AgentHostedService> logger)
    {
        _relayClient = relayClient;
        _processor = processor;
        _settings = settings;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting my-agent-chat...");

        // Validate configuration
        if (string.IsNullOrEmpty(_settings.Nostr.AgentPrivateKey))
            throw new InvalidOperationException("Nostr:AgentPrivateKey is required");
        if (string.IsNullOrEmpty(_settings.Nostr.AuthorizedNpub))
            throw new InvalidOperationException("Nostr:AuthorizedNpub is required");
        if (_settings.Nostr.Relays.Length == 0)
            throw new InvalidOperationException("Nostr:Relays must have at least one relay");

        var agentPrivKey = KeyHelper.ParsePrivateKey(_settings.Nostr.AgentPrivateKey);
        var agentPubKey = Convert.ToHexString(agentPrivKey.CreateXOnlyPubKey().ToBytes()).ToLowerInvariant();

        _logger.LogInformation("Agent pubkey: {PubKey}", agentPubKey);
        _logger.LogInformation("Authorized sender: {AuthNpub}", _settings.Nostr.AuthorizedNpub);
        _logger.LogInformation("OpenCode API: {BaseUrl}", _settings.OpenCode.BaseUrl);

        // Wire up gift wrap handler
        _relayClient.GiftWrapReceived += async (sender, giftWrap) =>
        {
            try
            {
                await _processor.HandleGiftWrapAsync(giftWrap, _relayClient.Client, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing gift wrap {Id}", giftWrap.Id);
            }
        };

        // Connect to relays
        await _relayClient.ConnectAsync(agentPrivKey, ct);

        _logger.LogInformation("Agent is running. Waiting for DMs...");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Stopping my-agent-chat...");
        await _relayClient.DisposeAsync();
    }
}
