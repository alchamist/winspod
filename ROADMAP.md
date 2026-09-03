# Roadmap

Not commitments or a sequence — a menu, roughly grouped by what they depend
on. Pick from either list independently; nothing here blocks anything else
unless noted.

## Infrastructure

1. **Push to GitHub.** Currently only local commits. Everything else here
   assumes the remote is caught up.

2. **Docker.** Straightforward — multi-stage build (`sdk:8.0` to build,
   `aspnet:8.0` to run), volume-mount the `LocalApplicationData/winspod` data
   dir so player data survives container recreation, `HEALTHCHECK` wired to
   `/healthz` (already built). Main design decision: move `app.config`
   settings (port, talker name, etc.) to environment-variable overrides so
   the same image works across deployments without a rebuild — small change,
   worth doing as part of this rather than before it.

3. **Browser access via a WebSocket bridge.** A small piece (WebSocket
   endpoint on the existing Kestrel host, proxying bytes to/from the telnet
   socket; `xterm.js` on the front end for a real terminal in a `<canvas>`)
   that solves two things at once:
   - Gives non-technical players a "click a link" way in, no client install.
   - **Fixes the Cloudflare Tunnel gap from earlier** — free `cloudflared`
     tunnels are HTTP(S)-only; raw TCP telnet needs the paid Spectrum
     product. A WebSocket is HTTP traffic (an upgraded HTTP connection), so
     it tunnels through a free Cloudflare Tunnel with no extra product
     needed. This becomes the actual answer to "how do people reach this
     without port-forwarding," not just a nice-to-have.
   Depends on nothing else here; could genuinely happen before Docker.

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

5. **TLS-wrapped telnet, as a lower-effort alternative to #4.** If full SSH
   is more than you want to build, `telnets`-style (TLS over the same
   socket) gets you the encryption win without inventing an SSH server —
   `SslStream` wrapping the existing `NetworkStream` in `Connection.cs` is a
   much smaller change. Worth doing even if SSH also happens eventually,
   since it's cheap and closes the cleartext-password gap on its own.

6. **`System.Threading.Channels`-based command queue**, replacing the
   `SemaphoreSlim` `BigLock`. Only matters if concurrent load becomes real —
   today's fix (async I/O, still fully serialized) already solved the actual
   problem (one OS thread parked per idle connection). This is about
   processing throughput under real concurrency, not correctness — skip it
   until there's a reason to believe it's needed.

## Commands (from the ew-too diff)

Ordered roughly by how much they'd actually change day-to-day play, per the
comparison against `github.com/talkers/ew-too`'s `clist.h`:

1. **`converse` mode** — type freely without prefixing every line with
   `'`/`;`. Standout gap for a chat-first talker; no equivalent found in
   Winspod's command list.
2. **`linewrap`/`wordwrap`/`set_term_width`** — per-player output width.
   Matters more now that a browser client (#3 above) is on the table, since
   terminal width assumptions get less predictable, not less.
3. **`connect_room`** — per-player custom login room vs. today's single
   system-wide `DefaultLoginRoom`.
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
