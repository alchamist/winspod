# Roadmap

Not commitments or a sequence — a menu, roughly grouped by what they depend
on. Pick from either list independently; nothing here blocks anything else
unless noted.

## Infrastructure

1. **Push to GitHub.** Currently only local commits. Everything else here
   assumes the remote is caught up.

2. ~~Docker~~ — **done.** `Dockerfile`/`docker-compose.yml`, multi-stage
   build, `HEALTHCHECK` wired to `/healthz`, and `MUD_PORT`/`MUD_HTTP_ENABLED`/
   `MUD_HTTP_PORT`/`MUD_TALKER_NAME`/`MUD_TALKER_ADDRESS`/`MUD_TALKER_EMAIL`
   environment-variable overrides (`Program.cs`) so the same image works
   across deployments without a rebuild. Built and run for real (colima on
   this dev machine, since no Docker was installed) — hit one real bug along
   the way: `VOLUME` creates its mount point owned by `root` if the path
   doesn't already exist in the image first, which denied writes from the
   non-root container user and crashed on the first connection. Fixed by
   creating and `chown`ing the data directory before the `VOLUME`
   instruction. Verified telnet login, the API/`/healthz` through mapped
   ports, and that an account survives a full `docker rm` + recreate against
   the same named volume.

3. ~~Browser access via a WebSocket bridge~~ — **done.**
   `Api/TelnetWebSocketBridge.cs`: a WebSocket endpoint on the existing
   Kestrel host (`/ws`, only active when `HTTPEnabled`) that proxies bytes to
   the telnet listener via a plain loopback TCP connection - the game engine
   (`Connection.cs` and the ~60 places checking `socket.Connected`/`Close`)
   never has to know a browser is involved. `wwwroot/index.html` is a bare
   `xterm.js` proof-of-concept: real terminal, real game, line-buffered
   input, `xterm-addon-fit` for full-screen sizing. Telnet IAC negotiation is
   stripped entirely before reaching the browser (which doesn't speak
   Telnet); the echo-off/on sequences `sendEchoOff`/`sendEchoOn` send are
   translated into an `ECHO-OFF`/`ECHO-ON` text marker the front end watches
   for to toggle local echo during password entry. Also the actual answer to
   the Cloudflare Tunnel gap noted below - a WebSocket is HTTP traffic (an
   upgraded HTTP connection), so it tunnels through a free Cloudflare Tunnel
   with no paid TCP product needed, unlike raw telnet. The friendlier
   newcomer wrapper (help sidebar, clickable commands) on top of this bare
   terminal is still open - a separate, later pass, not part of this item.
   Gotcha hit and fixed along the way: the echo markers had literal `0x01`
   bytes embedded in the string literals (invisible in a normal file read,
   only visible via a raw hex dump), silently breaking the front end's exact
   string match and leaking passwords in plaintext until caught by testing
   against a real browser.

4. **SSH access.** A bigger lift than the WebSocket bridge — needs an
   embedded SSH server (e.g. wrapping `SSH.NET`'s server-side pieces, which
   is less turnkey than its client side) authenticating against the
   existing player accounts and piping an interactive shell into the same
   command loop `Connection.RunAsync` already drives. Worth it for two
   concrete reasons, not just novelty: (a) it's **encrypted** — telnet
   currently sends passwords in cleartext, SSH doesn't — and (b) a real SSH
   client ships on every modern OS by default, unlike `telnet`, which we
   had to `brew install` earlier this session because macOS doesn't include
   it anymore. Same true of most Linux distros and Windows now defaults to
   an SSH *client* but not `telnet`. This is the same shape of upgrade the
   Dominion talker (see below) made, and for the same reasons.

5. ~~TLS-wrapped telnet, as a lower-effort alternative to #4~~ — **done.**
   Runs as an *additional* listener (`Server.cs`'s `ListenTlsAsync`) on its
   own port (`MUD_TLS_PORT`, default 4443), enabled via `MUD_TLS_ENABLED` -
   not a replacement for plain telnet on 4000, so existing clients and the
   WebSocket bridge's loopback connection (deliberately left plain - see its
   own comments) keep working unchanged. `Connection`'s constructor now
   takes a `Stream` rather than building a `NetworkStream` itself
   internally, so an already-authenticated `SslStream` slots in exactly like
   a plain connection everywhere else in the game - no other code needed to
   change. `TlsCertificate.cs` generates a self-signed certificate once and
   persists it in the data volume (a real CA-issued cert isn't the point
   here; a telnet client doing opportunistic TLS won't validate the chain
   the way a browser does anyway - this is about stopping passive
   eavesdropping, not proving server identity). Verified end-to-end
   (TLS 1.3, full registration/login/room-look flow) and confirmed plain
   telnet on 4000 keeps working unaffected.
   Full SSH (#4 above) is still on the table if a specific need for it shows
   up, but this already closes the actual cleartext-password gap.

6. **`System.Threading.Channels`-based command queue**, replacing the
   `SemaphoreSlim` `BigLock`. Only matters if concurrent load becomes real —
   today's fix (async I/O, still fully serialized) already solved the actual
   problem (one OS thread parked per idle connection). This is about
   processing throughput under real concurrency, not correctness — skip it
   until there's a reason to believe it's needed.

## Commands (from the ew-too diff)

Ordered roughly by how much they'd actually change day-to-day play, per the
comparison against `github.com/talkers/ew-too`'s `clist.h`. Verified against
the live `cmd all` listing (213 commands) and `commands/cmdList.dat` - unlike
the list subsystem below, none of these five are hiding behind a different
name or a subcommand of the generic `set` (which only covers social-profile
fields: jabber/icq/msn/yahoo/skype/email/URLs/jetlag/favourites). Confirmed
gaps, not diff artifacts:

1. **`converse` mode** — type freely without prefixing every line with
   `'`/`;`. Standout gap for a chat-first talker; no equivalent found in
   Winspod's command list.
2. **`linewrap`/`wordwrap`/`set_term_width`** — per-player output width.
   Matters more now that a browser client (#3 above) is on the table, since
   terminal width assumptions get less predictable, not less.
3. ~~`connect_room`~~ — **done**, as `connectroom` (Winspod's commands don't
   use underscores — `roomadd`, `roomlock`, `logonmsg`, etc. — so it follows
   that style rather than ew-too's). `connectroom <room>`/`connectroom`/
   `connectroom off` (`RoomCommands.cs`), applied at login. Verified against
   a real room; an initial test against "jail" looked broken but turned out
   to be `Heartbeat.cs`'s pre-existing auto-release-from-jail logic correctly
   ejecting a non-jailed player, unrelated to this feature.
4. **`nopager`** — toggle for disabling output paging on long text; depends
   on whether Winspod pages long output at all today (needs checking).
5. **`iacga`** — Telnet IAC Go-Ahead toggle for older/dumb clients. Cheap,
   low-priority, include if doing a general Telnet-handling pass.
6. **`site`/`netstat`** (admin) — inspecting/grouping connections by
   IP/site, beyond today's `ipban`/`ipunban`.

~~Named-list subsystem~~ — **already present**, corrected after actually
running `flist` in-game rather than trusting the command-name diff alone.
`list`/`flist` (`Connection.cs:cmdList`) exposes the full per-target flag
matrix ew-too split across many verbs (`friend`/`find`/`inform`/`noisy`/
`ignore`/`bar`/`beep`/`block`/`mailblock`/`grab`/`key`), plus the named
groupings (`All`/`Friends`/`Staff`) ew-too also had; `where <player>` covers
`find`. The earlier command-name diff missed this because `flist` is one
`cmdList.dat` entry hiding a whole subsystem behind it — a reminder that
this diff was done by comparing tables, not by actually exercising the
commands, so treat the rest of this list the same way: a starting point to
verify in-game, not a confirmed gap list.

Explicitly **not** planned: ew-too's `malloc`/`dfstats`/`defrag`/`dtb`/`dtk`
(manual C heap debugging — meaningless under the .NET GC) and `crash`
(a test hook for exercising `angel.c`, which we're not reinventing — see
`SdNotify.cs`/`deploy/mudserver.service.example` instead).
