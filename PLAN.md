# my-agent-chat — Implementation Plan

## Overview

A C# .NET 8 console application that bridges Nostr NIP-17 (Gift Wrap) private DMs with OpenCode's HTTP API, creating an AI agent you can chat with from your phone over Nostr. The agent has full OS access via OpenCode's tool system (shell, file editing, code search, etc.).

## Architecture

```
┌─────────────────────┐
│  Your Phone          │
│  (Amethyst/etc)      │
│  npub: authorized    │
└─────────┬───────────┘
          │ NIP-17 Gift Wrap DM
          v
┌─────────────────────┐
│  Nostr Relays        │
│  (kind 1059 events)  │
└─────────┬───────────┘
          │
          v
┌─────────────────────────────────────────────┐
│  my-agent-chat (.NET 8 Console App)          │
│                                              │
│  ┌──────────────────┐  ┌──────────────────┐ │
│  │ NostrRelayClient │  │ Nip17Decryptor   │ │
│  │ (subscribe 1059) │──│ (unwrap 3 layers)│ │
│  └──────────────────┘  └────────┬─────────┘ │
│                                 │            │
│                    verify sender = auth npub │
│                                 │            │
│                    ┌────────────v─────────┐  │
│                    │ MessageProcessor     │  │
│                    │ (bridge logic)       │  │
│                    └────────────┬─────────┘  │
│                                 │            │
│              ┌─────────────────┐│            │
│              │ SessionManager  ││            │
│              │ (npub→session)  ││            │
│              └─────────────────┘│            │
│                                 │            │
│                    ┌────────────v─────────┐  │
│                    │ OpenCodeClient       │  │
│                    │ POST /session/:id/   │  │
│                    │      message         │  │
│                    └────────────┬─────────┘  │
│                                 │            │
│                    ┌────────────v─────────┐  │
│                    │ EventPublisher       │  │
│                    │ (wrap NIP-17 reply)  │  │
│                    └─────────────────────┘  │
└──────────────────────────────────────────────┘
          │
          v
┌─────────────────────┐
│  Nostr Relays        │
└─────────┬───────────┘
          │ NIP-17 DM reply
          v
┌─────────────────────┐
│  Your Phone          │
│  (sees AI response)  │
└─────────────────────┘
```

## OpenCode Integration

OpenCode runs as a headless server (`opencode serve`) and exposes an HTTP API on port 4096.

### Key Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/session` | `POST` | Create a new session `{ title? }` |
| `/session/:id/message` | `POST` | Send prompt (sync, blocks until response) |
| `/session/:id/prompt_async` | `POST` | Send prompt (async, returns 204, stream via SSE) |
| `/session/:id/message` | `GET` | Read messages (query: `limit?`) |
| `/session/:id/abort` | `POST` | Abort current generation |
| `/event` | `GET` | SSE event stream for async monitoring |

### Sending a Message

```http
POST /session/:id/message
Content-Type: application/json

{
  "parts": [{ "type": "text", "text": "list all files in the current directory" }]
}
```

Response: `{ info: Message, parts: Part[] }`

### Server Startup

```bash
opencode serve --port 4096 --dangerously-skip-permissions
```

The `--dangerously-skip-permissions` flag is safe here because the only user is the pre-authorized npub.

### Authentication

Optional. Set `OPENCODE_SERVER_PASSWORD` env var to enable HTTP Basic Auth.

## Project Structure

```
my-agent-chat/
├── src/
│   └── MyAgentChat/
│       ├── Program.cs                  # Entry point, DI, wiring
│       ├── Nostr/
│       │   ├── NostrRelayClient.cs     # Relay connection + kind 1059 subscription
│       │   ├── Nip17Decryptor.cs       # 3-layer gift wrap decryption
│       │   └── EventPublisher.cs       # NIP-17 DM reply publishing
│       ├── OpenCode/
│       │   ├── OpenCodeClient.cs       # HTTP client for OpenCode API
│       │   └── SessionManager.cs       # Maps npub → OpenCode session ID
│       ├── Services/
│       │   └── MessageProcessor.cs     # Bridge: nostr msg → opencode → nostr reply
│       ├── Config/
│       │   └── AppSettings.cs          # Strongly-typed configuration
│       └── MyAgentChat.csproj
├── appsettings.json
├── Dockerfile
├── docker-compose.yml
├── PLAN.md
├── OPENCODE-NOSTR-FORK.md
└── README.md
```

## Components

### Copied from nostr-shorts-dvm (adapted)

| File | Source | Changes |
|------|--------|---------|
| `Nip17Decryptor.cs` | `nostr-shorts-dvm/Nostr/Nip17Decryptor.cs` | Namespace change only |
| `EventPublisher.cs` | `nostr-shorts-dvm/Nostr/EventPublisher.cs` | Strip video/DVM logic, keep only `SendDirectMessage()` |
| `NostrRelayClient.cs` | `nostr-shorts-dvm/Nostr/NostrRelayClient.cs` | Namespace change only |

### New Components

| File | Responsibility |
|------|---------------|
| `OpenCodeClient.cs` | HTTP client wrapping OpenCode's REST API. Creates sessions, sends messages, reads responses. Uses `HttpClient` with configurable base URL. |
| `SessionManager.cs` | Maintains a mapping of nostr pubkey → OpenCode session ID. Stores in memory with optional SQLite persistence. Enables continuous conversations. |
| `MessageProcessor.cs` | The core bridge: receives decrypted nostr message → checks authorization → looks up or creates OpenCode session → sends prompt → gets response → publishes NIP-17 reply. Handles errors gracefully with DM error replies. |
| `AppSettings.cs` | Config classes: `NostrSettings` (AgentPrivateKey, AuthorizedNpub, Relays[]), `OpenCodeSettings` (BaseUrl, Password?), `GeneralSettings` (MaxResponseLength). |

## Dependencies

```xml
<PackageReference Include="NNostr.Client" Version="0.0.54" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.7" />
```

`HttpClient` is built-in to .NET — no extra package needed for OpenCode API calls.

## Configuration

```json
{
  "Nostr": {
    "AgentPrivateKey": "nsec1...",
    "AuthorizedNpub": "npub1...",
    "Relays": [
      "wss://relay.damus.io",
      "wss://relay.primal.net",
      "wss://nos.lol"
    ]
  },
  "OpenCode": {
    "BaseUrl": "http://localhost:4096",
    "Password": null
  },
  "General": {
    "MaxResponseLength": 4000,
    "SkipOlderThanMinutes": 5
  }
}
```

All settings can be overridden via environment variables (standard .NET config binding):
- `Nostr__AgentPrivateKey`
- `Nostr__AuthorizedNpub`
- `OpenCode__BaseUrl`
- etc.

## Docker Deployment

```yaml
# docker-compose.yml
version: "3.8"
services:
  opencode:
    image: ghcr.io/anomalyco/opencode:latest  # or build from source
    command: ["serve", "--port", "4096", "--hostname", "0.0.0.0", "--dangerously-skip-permissions"]
    ports:
      - "4096:4096"
    volumes:
      - ./workspace:/workspace  # directory OpenCode operates on
    working_dir: /workspace

  agent:
    build: .
    depends_on:
      - opencode
    environment:
      - Nostr__AgentPrivateKey=nsec1...
      - Nostr__AuthorizedNpub=npub1...
      - OpenCode__BaseUrl=http://opencode:4096
    restart: unless-stopped
```

## Message Flow (Detailed)

1. **Receive**: `NostrRelayClient` gets kind 1059 event from relay
2. **Decrypt**: `Nip17Decryptor` unwraps 3 layers:
   - Gift Wrap (kind 1059) → decrypt with agent's nsec + sender's ephemeral pubkey
   - Seal (kind 13) → decrypt with agent's nsec + sender's real pubkey
   - Rumor (kind 14) → plaintext message content
3. **Authorize**: Verify sender pubkey matches `AuthorizedNpub`
4. **Skip old**: Ignore messages older than `SkipOlderThanMinutes`
5. **Session lookup**: `SessionManager` finds or creates an OpenCode session for this sender
6. **Prompt**: `OpenCodeClient` sends `POST /session/:id/message` with the message text
7. **Response**: OpenCode executes tools, returns response text
8. **Truncate**: If response exceeds `MaxResponseLength`, truncate with "...(truncated)" or split into multiple messages
9. **Reply**: `EventPublisher` wraps response in NIP-17 gift wrap and publishes to relays
10. **Error handling**: On any failure, send an error DM back (e.g., "Error: OpenCode unreachable")

## Special Commands

Optional built-in commands (handled before routing to OpenCode):

| Command | Action |
|---------|--------|
| `/status` | Reply with agent uptime, OpenCode status, session count |
| `/sessions` | List active OpenCode sessions |
| `/new` | Force-create a new OpenCode session (fresh conversation) |
| `/abort` | Abort current OpenCode generation |

Everything else goes to OpenCode as a prompt.

## Implementation Phases

### Phase 1: Project Setup
- Create .NET 8 console project with `Microsoft.Extensions.Hosting` and `NNostr.Client`
- Copy and adapt NIP-17 code from `nostr-shorts-dvm`
- Set up config classes and `appsettings.json`

### Phase 2: OpenCode Client
- Implement `OpenCodeClient.cs` with session create + message send
- Implement `SessionManager.cs` with in-memory session mapping
- Test against a running `opencode serve` instance

### Phase 3: Bridge Logic
- Implement `MessageProcessor.cs` — the core nostr ↔ OpenCode bridge
- Handle authorization, error replies, response truncation
- Wire up in `Program.cs`

### Phase 4: Docker
- Write `Dockerfile` for the agent
- Write `docker-compose.yml` with OpenCode sidecar
- Test full stack in Docker

### Phase 5: Polish
- Add special commands (`/status`, `/new`, `/abort`)
- Add SQLite session persistence (optional)
- Add logging and health checks
- End-to-end test from phone

## Security Considerations

- **Agent nsec** must be kept secret — use env vars in production, never commit to git
- **Sender verification** is critical — only process messages from `AuthorizedNpub`
- **`--dangerously-skip-permissions`** means OpenCode will execute any command without confirmation — acceptable because the only user is pre-authorized, but be aware the agent has full OS access
- **NIP-17 encryption** ensures messages are end-to-end encrypted on relays
- **Network isolation**: In Docker, only the agent talks to OpenCode (no external access to port 4096)
