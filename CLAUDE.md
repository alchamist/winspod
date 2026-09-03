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
to take effect, not just a restart.

## Architecture

- **`Server.cs`** — owns the telnet listening socket and process-wide
  counters (uptime, player counts, command-usage stats). Runs as a
  cancellable async loop (`RunAsync`), not the `Thread.Abort()`-based loop
  the original used (removed on modern .NET — throws
  `PlatformNotSupportedException`). `Restart()` cancels and reopens just the
  listening socket; connected players are unaffected.
- **`Connection.cs`** — one instance per connected player, `partial class`
  spread across most other `.cs` files in the project (`AdminCommands.cs`,
  `RoomCommands.cs`, `Mail.cs`, etc. all add methods to it — this is how the
  original codebase organized ~20 command groups, not a refactor artifact).
  `RunAsync()` is the per-connection I/O loop: async (`ReadLineAsync`), not
  the original's one-blocked-OS-thread-per-connection model. All game-state
  mutation is still fully serialized behind a single `BigLock`
  (`SemaphoreSlim(1,1)`, replacing the original `lock`/`Monitor` — Monitor
  locks can't wrap an `await`). This keeps identical single-writer semantics
  to the original; it does **not** add real concurrency, it just stops idle
  connections from parking an OS thread. See ROADMAP.md if genuine
  concurrent processing is ever needed (would mean a
  `System.Threading.Channels`-based single-consumer command queue instead).
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
- Passwords currently transit **in cleartext** over the telnet socket (only
  local echo is suppressed via IAC WILL/WONT ECHO — this was also true of
  the original). Worth keeping in mind before exposing this beyond a trusted
  network; see ROADMAP.md's transport-security items.

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
