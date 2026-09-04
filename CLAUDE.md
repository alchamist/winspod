# Winspod

A MUD/talker server, originally written for Windows/.NET Framework 3.5, ported
to cross-platform .NET 8. Currently in active modernization — see
[ROADMAP.md](ROADMAP.md) for planned work.

## Lineage

Not a fork of any classic MUD codebase (Diku/Merc/ROM, LPMud, TinyMUD/MOO).
The author's own comments in the source (`AnsiColour.cs`: `// Need to add the
old ewtoo commands`; `help/version.txt`: thanks to a beta tester "for Nuts
4!") confirm it descends in spirit from **ew-too** and **NUTS** — the two
dominant codebases of the UK "talker" scene (chat-focused MUD variants, as
distinct from the US-rooted TinyMUD/MUSH lineage). Winspod itself is an
original C# reimplementation, not a direct port of that C source.
The upstream reference is https://github.com/talkers/ew-too — useful for
spotting missing features (see ROADMAP.md) and understanding conventions
(e.g. `angel.c`, the crash/hang supervisor Winspod's `SdNotify.cs` +
systemd's watchdog replace).

## Build & run

```bash
cd MudServer
dotnet build
dotnet run --no-build
```

Requires the .NET 8 SDK (`dotnet --version`). No solution-level build needed;
work directly in `MudServer/`.

Connect with a real Telnet client — `nc` will connect but won't interpret the
Telnet IAC echo-off/echo-on bytes sent during password entry, so login looks
garbled (`??` marks) over plain `nc`. `telnet localhost 4000` is correct;
`brew install telnet` if missing (not installed on macOS by default).

Player/room/log data is *not* in the repo — it's written at runtime to
`Environment.SpecialFolder.LocalApplicationData` + `winspod`
(`~/Library/Application Support/winspod` on macOS, `~/.local/share/winspod`
on Linux, `%LOCALAPPDATA%\winspod` on Windows). First registered username
becomes the system admin (original ew-too/Winspod bootstrap behavior,
unchanged).

The web API (see below) is off by default. Toggle `HTTPEnabled` in
`MudServer/app.config` (`True`/`False`) and rebuild — it's read into
`MudServer.dll.config` at build time, so a config-only edit needs a rebuild
to take effect, not just a restart. (Under Docker, use `MUD_HTTP_ENABLED`
instead — see below — which takes effect at container start, no rebuild.)

### Docker

```bash
docker compose up --build
```

No Docker Desktop needed on macOS — `brew install colima docker` +
`colima start` gives a CLI-only engine, which is what this was built and
verified against (no GUI installed on this dev machine).
`MUD_PORT`/`MUD_HTTP_ENABLED`/`MUD_HTTP_PORT`/`MUD_TALKER_NAME`/
`MUD_TALKER_ADDRESS`/`MUD_TALKER_EMAIL` env vars override the equivalent
`app.config` settings at startup (`Program.cs`'s `ApplyEnvironmentOverrides`)
— deliberately just those six, not a general mechanism, since those are what
actually vary per-deployment. Player data persists via the named volume in
`docker-compose.yml`, mounted at the same `LocalApplicationData` path the
non-Docker setup uses (under the container's `mudserver` user's `$HOME`).

## Architecture

- **`Server.cs`** — owns the telnet listening socket and process-wide
  counters (uptime, player counts, command-usage stats). Runs as a
  cancellable async loop (`RunAsync`), not the `Thread.Abort()`-based loop
  the original used (removed on modern .NET — throws
  `PlatformNotSupportedException`). `Restart()` cancels and reopens just the
  listening socket; connected players are unaffected. Also runs
  `ListenTlsAsync`, an *additional* listener (not a replacement) for
  TLS-wrapped telnet when `TelnetTlsEnabled` is set — see the TLS bullet
  below.
- **`Connection.cs`** — one instance per connected player, `partial class`
  spread across most other `.cs` files in the project (`AdminCommands.cs`,
  `RoomCommands.cs`, `Mail.cs`, etc. all add methods to it — this is how the
  original codebase organized ~20 command groups, not a refactor artifact).
  `RunAsync()` is the per-connection I/O loop: async, not the original's
  one-blocked-OS-thread-per-connection model. Reads via
  `ReadBoundedLineAsync`/`SkipTelnetCommandAsync` — a real byte-level Telnet
  IAC parser (there wasn't one originally; negotiation bytes just flowed
  into `StreamReader`'s decoding and got crudely patched around) that also
  caps a line at `MaxLineLength` rather than buffering an unbounded amount
  waiting for a terminator. `ReadStream`/`Writer` are typed as the base
  `Stream`/`StreamWriter` classes, not `NetworkStream`-specific, so the same
  connection code runs identically over plain telnet or a TLS-wrapped
  `SslStream` (see below) without knowing which. All game-state
  mutation — including the per-connection heartbeat tick, which runs on its
  own independent `System.Timers.Timer` thread and used to touch shared
  state without it — is fully serialized behind a single `BigLock`
  (`SemaphoreSlim(1,1)`, replacing the original `lock`/`Monitor` — Monitor
  locks can't wrap an `await`). This keeps identical single-writer semantics
  to the original; it does **not** add real concurrency, it just stops idle
  connections from parking an OS thread. See ROADMAP.md if genuine
  concurrent processing is ever needed (would mean a
  `System.Threading.Channels`-based single-consumer command queue instead).
- **TLS-wrapped telnet** (`Server.cs`'s `ListenTlsAsync` + `TlsCertificate.cs`) —
  an additional listener on `TelnetTlsPort` (default 4443, env
  `MUD_TLS_PORT`/`MUD_TLS_ENABLED`) alongside the plain telnet port, not a
  replacement for it. Completes an `SslStream` handshake per connection
  using a self-signed certificate (`TlsCertificate.cs`, generated once and
  persisted in the data volume — a real CA-issued cert isn't the point here,
  since a telnet client doing opportunistic TLS won't validate the chain the
  way a browser does; this is about stopping passive eavesdropping on
  cleartext passwords, not proving server identity), then hands the
  resulting stream to a normal `Connection` exactly like a plain accept
  would. Plain telnet on the original port, and the WebSocket bridge's own
  loopback connection to its separate internal port (see
  `Api/TelnetWebSocketBridge.cs` below, deliberately left plain), are both
  untouched.
- **`Api/TelnetWebSocketBridge.cs` + `wwwroot/`** — a WebSocket endpoint
  (`/ws`, mapped only when `HTTPEnabled`) that proxies a browser connection
  to the game via a plain loopback TCP connection to `Server.InternalBridgePort`
  (not the public telnet port), rather than teaching `Connection.cs` (and the
  ~60 places across the codebase checking `socket.Connected`/`Close` on it)
  about a second transport — from the game engine's side this is
  indistinguishable from any other telnet client. It connects to its own
  internal, loopback-only port rather than the public one specifically so it
  can send one preamble line first: the browser player's real IP
  (`context.Connection.RemoteIpAddress`), which `Server.ListenInternalAsync`
  reads and sets as `Connection.RemoteIpOverride` before the normal telnet
  session starts — without this, every bridged player's socket is the
  bridge's own loopback connection, so `ipban`/`list ip`/connection logging
  would see 127.0.0.1 for every browser player instead of their actual
  address. Strips Telnet IAC negotiation entirely before forwarding to the
  browser (which doesn't speak Telnet), translating the
  `sendEchoOff`/`sendEchoOn` sequences into an `ECHO-OFF`/`ECHO-ON` text
  marker the front end (`wwwroot/index.html`, a bare `xterm.js` +
  `xterm-addon-fit` proof of concept) watches for to toggle local echo
  during password entry. Also solves the free-tier Cloudflare Tunnel gap
  noted in ROADMAP.md: a WebSocket is HTTP traffic, so it tunnels through a
  free Cloudflare Tunnel with no paid TCP product needed, unlike raw telnet.
- **`Api/`** — `MudApiEndpoints.cs` + `Dtos.cs`: an ASP.NET Core minimal API
  on Kestrel, replacing the original hand-rolled `HttpListener` webserver
  (raw HTTP parsing, HTML built by string concatenation — deleted). Publishes
  game state as JSON (`/api/status`, `/api/who`, `/api/leaderboard`,
  `/api/players/{username}`) rather than owning "the website" — any front
  end builds against this. Only runs when `HTTPEnabled=True`; otherwise
  `Program.cs` runs a bare `Host` with just the telnet loop, no Kestrel.
- **`SdNotify.cs`** — minimal `sd_notify` (systemd watchdog protocol)
  implementation. `Server.Tick` (the existing 1-second timer) probes
  `Connection.BigLock` via `ProbeLivenessAsync` every tick and only pings
  `WATCHDOG=1` when the probe succeeds — if the game loop ever deadlocks
  while holding `BigLock`, this stops feeding the watchdog and (under
  systemd, via `WatchdogSec=` — see `deploy/mudserver.service.example`)
  the process gets killed and restarted. No-op everywhere `NOTIFY_SOCKET`
  isn't set (dev machine, `dotnet run`, non-systemd hosts). `GET /healthz`
  on the API exposes the same signal for Docker/monitoring/load balancers
  that aren't systemd.
- **`AppSettings.Designer.cs` / `app.config`** — kept as-is (legacy
  `System.Configuration.ApplicationSettingsBase`), works unmodified on
  modern .NET via the `System.Configuration.ConfigurationManager` NuGet
  package. Not migrated to `appsettings.json` — would touch ~12 files for
  no functional gain; only worth doing alongside the Docker work, where
  environment-variable overrides become useful (see ROADMAP.md).

## Gotchas found only by actually building/running (not by reading code)

- `Thread.Abort()` (shutdown/restart) — compiles fine, throws at runtime on
  modern .NET. Fixed via cancellation (`Server.cs`).
- `PerformanceCounter("Memory", "Available Bytes")` in the admin `stats all`
  command (`Connection.cs`) — Windows-only, throws
  `PlatformNotSupportedException` elsewhere even with the compat package.
  Replaced with `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`.
- `InvariantGlobalization=true` (added for the SDK-style project) silently
  broke all `DateTime.ToString()`/`ToShortDateString()` output to a generic
  `MM/dd/yyyy`-ish format, surfacing as US-style dates in-game. Removed;
  culture is instead pinned explicitly to `en-GB` in `Program.cs` so the
  display is consistent regardless of the deployment host's own locale.
- The **plain telnet port** (4000) still transits passwords in cleartext by
  design (only local echo is suppressed via IAC WILL/WONT ECHO — this was
  also true of the original) — that port is kept exactly as-is for backward
  compatibility. TLS-wrapped telnet (see Architecture above) is available as
  an *additional* port when you want encryption; it doesn't change 4000.
  Passwords themselves are hashed at rest (`Player.cs`'s `SetPassword`/
  `checkPassword`, PBKDF2) — a separate concern from transit encryption.
- Docker's `VOLUME` instruction creates its mount point owned by `root` if
  the path doesn't already exist in the image — silently denying writes from
  a non-root container user and crashing the game loop on the first incoming
  connection (`UnauthorizedAccessException` in `Room.SaveRoom`). The
  `Dockerfile` creates and `chown`s the data directory before declaring
  `VOLUME`; if that ordering ever gets "cleaned up," this comes back.
- A player who logs off with `quit` triggers a harmless but noisy
  `ObjectDisposedException` (`Connection.RunAsync` line ~212, trying to
  `Flush()` a `Writer` that `cmdQuit` already closed, before the connection's
  removed from the broadcast list). Pre-existing ordering issue, already
  caught and logged — not introduced by the port, not yet fixed.
- A string literal can contain bytes that never show up in a normal file
  read. The WebSocket bridge's `ECHO-OFF`/`ECHO-ON` marker constants ended
  up with literal `0x01` bytes wrapped around the text (`"\x01ECHO-OFF\x01"`)
  from an earlier draft — invisible via `Read`, only visible via a raw byte
  dump (`cat -A` / hex) — which silently broke the front end's exact string
  match and leaked passwords in plaintext until caught by testing against a
  real browser, not just the code.
- `heartbeat_Elapsed` (`Heartbeat.cs`) runs on its own independent
  `System.Timers.Timer` thread, once per second per connection, completely
  unsynchronized with that connection's own async loop — and it was the
  *only* place in the whole codebase touching shared state (the static
  `connections` list, other connections' `Writer`, room/player data) without
  going through `BigLock` first. Reproducibly caused "random" disconnects
  once a real room layout gave it enough concurrent work to do. Now
  acquires `BigLock` like everywhere else.

## Conventions to follow when editing

- Command dispatch is driven by `commands/cmdList.dat` (loaded per-connection
  in `FileMethods.cs`'s `loadCommands()`), not a C# attribute/reflection
  system — adding a command means adding both a row there and a handler
  method on the `Connection` partial class.
- Prefer `Path.Combine`/`Path.DirectorySeparatorChar` over hardcoded
  separators (the existing code is already consistent about this — keep it
  that way, it's part of why the port was straightforward).
- Don't reach for `Thread`/blocking I/O in new connection-handling code —
  follow `Connection.RunAsync`'s async pattern and acquire `BigLock` via
  `await BigLock.WaitAsync()` / `finally { BigLock.Release(); }`, not `lock`.
