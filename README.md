# my-agent-chat

A C# .NET 8 console application that bridges Nostr NIP-17 (Gift Wrap) private DMs with OpenCode's HTTP API. Chat with an AI agent from your phone over Nostr — the agent has full OS access via OpenCode's tool system (shell, file editing, code search, etc.).

## How It Works

```
Phone (Scramble/etc) → NIP-17 DM → Nostr Relays → my-agent-chat → OpenCode API → AI Response → NIP-17 DM → Phone
```

1. You send a private DM from your phone to the agent's npub
2. The agent decrypts the NIP-17 gift wrap (3 layers: gift wrap → seal → rumor)
3. Verifies the sender is the authorized npub
4. Forwards the message to OpenCode's HTTP API
5. Wraps the response in NIP-17 and sends it back as a DM

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [OpenCode](https://opencode.ai) installed and available in PATH
- [Docker](https://www.docker.com/) (optional, for containerized deployment)
- A Nostr client on your phone (e.g. Scramble)

## Quick Start

### 1. Generate agent keys

Generate a new nostr keypair for the agent (nsec/npub). You can use any nostr key generator.

### 2. Create a `.env` file

```
NOSTR_AGENT_PRIVATE_KEY=nsec1...
NOSTR_AUTHORIZED_NPUB=npub1...
```

### 3. Start OpenCode

```bash
opencode serve --port 4096
```

### 4. Run the agent

**With Docker (recommended):**

```bash
docker compose up -d --build agent
```

**Without Docker:**

```bash
cd src/MyAgentChat
dotnet run -- --Nostr:AgentPrivateKey=nsec1... --Nostr:AuthorizedNpub=npub1...
```

Or set environment variables:

```bash
export Nostr__AgentPrivateKey=nsec1...
export Nostr__AuthorizedNpub=npub1...
dotnet run --project src/MyAgentChat
```

### 5. Chat

Add the agent's npub as a contact in your Nostr client and send it a DM.

## Configuration

All settings can be configured via `appsettings.json`, environment variables, or command-line arguments.

| Setting | Env Variable | Default | Description |
|---------|-------------|---------|-------------|
| `Nostr:AgentPrivateKey` | `Nostr__AgentPrivateKey` | — | Agent's nsec or hex private key (required) |
| `Nostr:AuthorizedNpub` | `Nostr__AuthorizedNpub` | — | Only process DMs from this npub (required) |
| `Nostr:Relays` | — | damus, primal, nos.lol | Relay WebSocket URLs |
| `OpenCode:BaseUrl` | `OpenCode__BaseUrl` | `http://localhost:4096` | OpenCode API URL |
| `OpenCode:Password` | `OpenCode__Password` | — | HTTP Basic Auth password (optional) |
| `General:MaxResponseLength` | — | 4000 | Truncate responses longer than this |
| `General:SkipOlderThanMinutes` | — | 5 | Ignore DMs older than this on startup |

## Special Commands

Send these as DMs to the agent instead of routing to OpenCode:

| Command | Action |
|---------|--------|
| `/status` | Agent uptime, session count, OpenCode URL |
| `/sessions` | Number of active OpenCode sessions |
| `/new` | Start a fresh OpenCode session |
| `/abort` | Abort the current OpenCode generation |

## Project Structure

```
src/MyAgentChat/
├── Program.cs                  # Entry point + hosted service
├── Config/AppSettings.cs       # Configuration classes
├── Nostr/
│   ├── Nip17Decryptor.cs       # NIP-17 gift wrap decryption
│   ├── NostrRelayClient.cs     # Relay connection + subscription
│   ├── EventPublisher.cs       # NIP-17 DM reply publishing
│   ├── KeyHelper.cs            # nsec/npub key parsing
│   └── Bech32.cs               # Bech32 encode/decode
├── OpenCode/
│   ├── OpenCodeClient.cs       # HTTP client for OpenCode API
│   └── SessionManager.cs       # npub → session ID mapping
└── Services/
    └── MessageProcessor.cs     # Core bridge logic
```

## Security

- **Agent nsec** is kept in `.env` (git-ignored) — never committed to the repo
- **Sender verification** — only processes DMs from the configured `AuthorizedNpub`
- **NIP-17 encryption** — all messages are end-to-end encrypted on relays
- **No password needed** if OpenCode is only accessible on localhost
