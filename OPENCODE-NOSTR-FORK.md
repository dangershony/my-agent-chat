# OpenCode Nostr Fork — High-Level Plan

## Goal

Fork OpenCode (Go) and add Nostr as a native communication transport, so the TUI/CLI chat interface is replaced (or supplemented) by NIP-17 Gift Wrap DMs. This turns OpenCode into a nostr-native AI agent without needing a separate bridge application.

## Why Fork Instead of Bridge

| Aspect | Bridge (current approach) | Native Fork |
|--------|--------------------------|-------------|
| Latency | Extra hop (nostr → bridge → HTTP → OpenCode) | Direct (nostr → OpenCode) |
| Deployment | Two services (agent + OpenCode) | Single binary |
| Session handling | Bridge must map npub → session externally | Built into OpenCode's session system |
| Streaming | Must poll or proxy SSE | Native streaming to nostr as tokens arrive |
| Permissions | Coarse (skip all or none) | Could tie permissions to nostr pubkeys |
| Maintenance | Independent updates | Must track upstream OpenCode changes |

## OpenCode Architecture (Go)

OpenCode is a Go application with a client-server architecture:

```
opencode (Go binary)
├── cmd/            # CLI entry points (serve, run, web, etc.)
├── internal/
│   ├── app/        # Core application logic
│   ├── server/     # HTTP API server (gin/echo/stdlib)
│   ├── session/    # Session management
│   ├── message/    # Message handling
│   ├── agent/      # Agent definitions and tool routing
│   ├── tool/       # Built-in tools (bash, read, edit, glob, grep, etc.)
│   ├── provider/   # LLM provider integrations
│   └── tui/        # Terminal UI (bubbletea)
├── pkg/            # Public packages
└── main.go
```

The HTTP server (`internal/server/`) is the main integration point. It handles:
- Session CRUD
- Message sending/receiving
- SSE event streaming
- Tool permission management

## Implementation Plan

### Step 1: Understand the Server Layer

1. Fork `https://github.com/anomalyco/opencode`
2. Read `internal/server/` to understand how HTTP endpoints map to session/message operations
3. Identify the core interfaces: how a "message in" becomes "LLM prompt + tool execution + response out"
4. Map the data flow: `HTTP POST /session/:id/message` → session lookup → agent invocation → streaming response → HTTP response

### Step 2: Add Nostr Dependencies

Add Go nostr libraries to `go.mod`:

```
github.com/nbd-wtf/go-nostr      # Core nostr protocol
github.com/nbd-wtf/go-nostr/nip17 # NIP-17 gift wrap (if available)
github.com/nbd-wtf/go-nostr/nip44 # NIP-44 encryption
github.com/nbd-wtf/go-nostr/nip59 # NIP-59 seal/gift wrap
```

The `go-nostr` library is the standard Go nostr library and has good NIP support.

### Step 3: Create Nostr Transport Layer

New package: `internal/nostr/`

```go
// internal/nostr/transport.go
package nostr

type Transport struct {
    relays        []*nostr.Relay
    agentKey      *nostr.KeyPair
    authorizedPub string
    sessionStore  session.Store  // reuse OpenCode's session system
    app           *app.App       // reference to core app
}

func (t *Transport) Start(ctx context.Context) error {
    // 1. Connect to relays
    // 2. Subscribe to kind 1059 events for agent's pubkey
    // 3. On event: decrypt → authorize → route to session → respond
}

func (t *Transport) handleGiftWrap(ctx context.Context, event *nostr.Event) {
    // Decrypt NIP-17 (3 layers)
    // Verify sender
    // Find or create session (map npub → session ID)
    // Call app.SendMessage(sessionID, content)
    // Get response
    // Wrap in NIP-17 and publish
}
```

### Step 4: Integrate with OpenCode's Session System

The key insight is that OpenCode already has a session abstraction. The nostr transport just needs to:

1. **Create sessions**: Use the same `session.Create()` that the HTTP API uses
2. **Send messages**: Use the same `session.SendMessage()` path
3. **Get responses**: Subscribe to the same event bus the SSE endpoint uses
4. **Map identity**: `npub` → `session ID` (stored in a simple map or the session metadata)

```go
// In the message handler:
sess, err := t.sessionStore.GetOrCreate(senderPubkey)
if err != nil {
    // send error DM
    return
}

// This is the same call the HTTP handler makes
response, err := t.app.ProcessMessage(ctx, sess.ID, message.Content)
if err != nil {
    // send error DM
    return
}

// Wrap and send
t.sendGiftWrap(ctx, senderPubkey, response.Text())
```

### Step 5: Add CLI Configuration

New config section in `opencode.json`:

```json
{
  "nostr": {
    "enabled": true,
    "privateKey": "nsec1...",
    "authorizedPubkeys": ["npub1..."],
    "relays": [
      "wss://relay.damus.io",
      "wss://nos.lol"
    ]
  }
}
```

New CLI flag: `opencode serve --nostr` to enable the nostr transport alongside HTTP.

### Step 6: Add Streaming Support

Instead of waiting for the full response, stream tokens back as they arrive:

- OpenCode's event bus emits `message.part` events as the LLM generates tokens
- Batch tokens into chunks (e.g., every 2 seconds or every 500 chars)
- Send intermediate NIP-17 DMs with a "typing..." indicator
- Send final complete message when done

This gives a responsive chat experience on the phone.

### Step 7: Nostr-Native Permissions

Instead of `--dangerously-skip-permissions`, add nostr-aware permissions:

```go
// Permission request sent as a DM to the authorized npub
type PermissionRequest struct {
    Tool       string // "bash", "edit", etc.
    Args       string // command or file path
    SessionID  string
}

// User replies "yes" or "no" via DM
// Agent processes the reply and continues/aborts
```

This gives you interactive permission approval from your phone — a major advantage over the bridge approach.

## New CLI Commands

```bash
# Start with nostr transport
opencode serve --nostr

# Generate a new agent keypair
opencode nostr keygen

# Show agent's npub (to add as contact on phone)
opencode nostr pubkey

# Test nostr connectivity
opencode nostr ping
```

## File Changes Summary

| File | Change |
|------|--------|
| `go.mod` | Add `go-nostr` dependencies |
| `internal/nostr/transport.go` | New: Nostr transport layer |
| `internal/nostr/nip17.go` | New: Gift wrap encrypt/decrypt |
| `internal/nostr/config.go` | New: Nostr config types |
| `internal/server/server.go` | Modified: Start nostr transport alongside HTTP |
| `cmd/serve.go` | Modified: Add `--nostr` flag |
| `cmd/nostr.go` | New: `opencode nostr keygen/pubkey/ping` commands |
| `internal/config/config.go` | Modified: Add nostr config section |

## Upstream Contribution Strategy

1. Build as a clean feature branch with minimal changes to existing code
2. Keep the nostr transport as an opt-in module (no impact when disabled)
3. Write tests using nostr relay mocks
4. Open a PR to `anomalyco/opencode` with the feature flagged behind `--nostr`
5. If not accepted upstream, maintain as a fork with periodic rebases

## Risks and Mitigations

| Risk | Mitigation |
|------|-----------|
| OpenCode internals change frequently | Keep changes minimal, use public interfaces |
| `go-nostr` NIP-17 support incomplete | Implement gift wrap manually (it's not complex in Go) |
| Message size limits on relays | Chunk large responses into multiple DMs |
| Relay connectivity issues | Connect to multiple relays, retry logic |
| Session state lost on restart | Persist npub→session mapping (SQLite or file) |
| Merge conflicts with upstream | Keep nostr code isolated in `internal/nostr/` |
