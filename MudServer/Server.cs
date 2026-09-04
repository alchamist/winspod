using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace MudServer
{
    /// <summary>
    /// Owns the telnet listening socket and the small set of process-wide counters
    /// the game commands read (uptime, player counts, command-usage stats).
    ///
    /// Previously this ran on a dedicated <see cref="Thread"/> that was torn down with
    /// Thread.Abort() for both "shutdown" and "restart" admin commands. Thread.Abort is
    /// not supported on modern .NET (it throws PlatformNotSupportedException), so this
    /// is now a plain async loop driven by a CancellationToken: Restart() cancels just
    /// the listening socket (existing player connections are untouched, matching the
    /// original behaviour), and the hosting shutdown path uses Environment.Exit for
    /// parity with the original "socket.Close(); Environment.Exit(1);" behaviour.
    /// </summary>
    public static class Server
    {
        const int BacklogSize = 20;

        // Loopback-only, not exposed via Docker/env config - only the WebSocket bridge
        // (Api/TelnetWebSocketBridge.cs) ever connects here, so it's not something a
        // deployment needs to configure. See ListenInternalAsync for why this exists.
        public const int InternalBridgePort = 44100;

        public static DateTime startTime = DateTime.Now;
        public static Socket server;
        public static int shutdownSecs = -1;
        public static int playerCount = 0;
        public static int playerCountToday = 0;

        public static string userFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winspod");

        /// <summary>Result of the most recent liveness probe (see Connection.ProbeLivenessAsync), read by /healthz.</summary>
        public static bool LastLivenessOk { get; private set; } = true;
        public static DateTime LastLivenessCheck { get; private set; } = DateTime.Now;

        public struct cmdStats
        {
            public string cmd;
            public int count;
        }

        public static List<cmdStats> commandStats = new List<cmdStats>();

        static CancellationTokenSource listenerCts;
        static readonly System.Timers.Timer tickTimer = new System.Timers.Timer(1000);
        static int conCount = 0;

        static Server()
        {
            tickTimer.Elapsed += Tick;
            tickTimer.Start();
        }

        /// <summary>
        /// Runs until <paramref name="stoppingToken"/> is cancelled (process shutdown).
        /// Internally this may re-open the listening socket any number of times in
        /// response to Restart() without returning, so existing connections survive a restart.
        /// </summary>
        public static async Task RunAsync(CancellationToken stoppingToken)
        {
            // TLS-wrapped telnet (see ROADMAP) runs as an additional listener alongside
            // the plain one, not a replacement - existing telnet clients and the
            // WebSocket bridge's loopback connection (Api/TelnetWebSocketBridge.cs, which
            // deliberately stays plain - see its own comments) keep working unchanged.
            Task tlsTask = AppSettings.Default.TelnetTlsEnabled
                ? ListenTlsAsync(stoppingToken)
                : Task.CompletedTask;

            // Only needed when the WebSocket bridge itself is active (same condition
            // Program.cs uses to map it) - the bridge relays through this instead of the
            // public telnet port specifically so it can pass the browser player's real
            // IP through (see ListenInternalAsync).
            Task internalTask = AppSettings.Default.HTTPEnabled
                ? ListenInternalAsync(stoppingToken)
                : Task.CompletedTask;

            while (!stoppingToken.IsCancellationRequested)
            {
                using (listenerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    await ListenAsync(listenerCts.Token);
                }
            }

            await tlsTask;
            await internalTask;
        }

        /// <summary>
        /// A second telnet-protocol listener, identical to the public one except it's
        /// bound to loopback only and expects one extra line before the normal telnet
        /// session starts: the real IP of the browser player the WebSocket bridge is
        /// relaying for. Without this, every bridged connection is the bridge's own
        /// loopback socket to the game, so ipban/list ip/connection logging would all
        /// see 127.0.0.1 for every browser player instead of their actual address.
        /// Trusted unconditionally - nothing but our own bridge, running in the same
        /// process, can ever reach a socket bound to loopback only.
        /// </summary>
        static async Task ListenInternalAsync(CancellationToken token)
        {
            Socket internalServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            internalServer.Bind(new IPEndPoint(IPAddress.Loopback, InternalBridgePort));
            internalServer.Listen(BacklogSize);

            using (token.Register(() => { try { internalServer.Close(); } catch { } }))
            {
                try
                {
                    while (true)
                    {
                        Socket conn = await internalServer.AcceptAsync(token);
                        _ = HandleInternalConnectionAsync(conn, token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }
        }

        static async Task HandleInternalConnectionAsync(Socket conn, CancellationToken token)
        {
            NetworkStream stream = new NetworkStream(conn, true);
            string realIp;
            try
            {
                realIp = await ReadBridgePreambleAsync(stream, token);
            }
            catch (Exception e)
            {
                Connection.logError("Internal bridge connection preamble failed: " + e.Message, "Bridge");
                try { conn.Close(); } catch { }
                return;
            }

            var connection = new Connection(conn, Interlocked.Increment(ref conCount), stream);
            connection.RemoteIpOverride = realIp;
            await connection.RunAsync();
        }

        static async Task<string> ReadBridgePreambleAsync(NetworkStream stream, CancellationToken token)
        {
            var sb = new System.Text.StringBuilder();
            byte[] one = new byte[1];
            while (sb.Length < 64)
            {
                int read = await stream.ReadAsync(one, 0, 1, token);
                if (read == 0)
                    throw new IOException("Connection closed before preamble completed");
                if (one[0] == (byte)'\n')
                    break;
                if (one[0] != (byte)'\r')
                    sb.Append((char)one[0]);
            }
            return sb.ToString();
        }

        static async Task ListenTlsAsync(CancellationToken token)
        {
            X509Certificate2 cert;
            try
            {
                cert = TlsCertificate.LoadOrCreate();
            }
            catch (Exception e)
            {
                Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] TLS telnet disabled - could not load/create a certificate: " + e.Message);
                return;
            }

            int portNumber = AppSettings.Default.TelnetTlsPort;
            Socket tlsServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tlsServer.Bind(new IPEndPoint(IPAddress.Any, portNumber));
            tlsServer.Listen(BacklogSize);
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] TLS telnet listening on port " + portNumber);

            using (token.Register(() => { try { tlsServer.Close(); } catch { } }))
            {
                try
                {
                    while (true)
                    {
                        Socket conn = await tlsServer.AcceptAsync(token);
                        _ = HandleTlsConnectionAsync(conn, cert, token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }
        }

        /// <summary>
        /// Completes the TLS handshake for one accepted connection, then hands it off to
        /// a normal Connection exactly like a plain telnet accept would - from that point
        /// on the game engine has no idea this connection is encrypted.
        /// </summary>
        static async Task HandleTlsConnectionAsync(Socket conn, X509Certificate2 cert, CancellationToken token)
        {
            SslStream ssl = new SslStream(new NetworkStream(conn, true), false);
            try
            {
                await ssl.AuthenticateAsServerAsync(cert, false, SslProtocols.None, false);
            }
            catch (Exception e)
            {
                string remote;
                try { remote = conn.RemoteEndPoint?.ToString() ?? "unknown"; } catch { remote = "unknown"; }
                Connection.logError("TLS handshake failed from " + remote + ": " + e.Message, "TLS");
                try { conn.Close(); } catch { }
                return;
            }

            var connection = new Connection(conn, Interlocked.Increment(ref conCount), ssl);
            await connection.RunAsync();
        }

        static async Task ListenAsync(CancellationToken token)
        {
            int portNumber = AppSettings.Default.Port;

            Version vrs = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Winspod II " + vrs.ToString());
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Initialising");

            server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            server.Bind(new IPEndPoint(IPAddress.Any, portNumber));
            server.Listen(BacklogSize);
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Socket active. Listening for connections on port " + portNumber.ToString());
            Console.WriteLine("Application data storage folder: " + userFilePath);

            // No-op unless actually running under systemd (NOTIFY_SOCKET set) - tells a
            // Type=notify unit the service has finished starting.
            SdNotify.Ready();

            // Closing the socket is what unblocks AcceptAsync below when we're asked to stop.
            using (token.Register(() => { try { server.Close(); } catch { } }))
            {
                try
                {
                    while (true)
                    {
                        Socket conn = await server.AcceptAsync(token);
                        var connection = new Connection(conn, Interlocked.Increment(ref conCount));
                        _ = connection.RunAsync();
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }
        }

        // This is `async void`, normally worth avoiding, but it's the standard exception:
        // a timer event handler has nowhere to hand back a Task to anyway. Any exception
        // here would otherwise vanish silently, so ProbeLivenessAsync is careful to only
        // ever return true/false, never throw.
        static async void Tick(object sender, ElapsedEventArgs e)
        {
            if (shutdownSecs > -1)
            {
                if (shutdownSecs-- == 0)
                {
                    tickTimer.Stop();
                    Console.WriteLine("Shutdown time reached - exiting");
                    Environment.Exit(0);
                }
            }

            if (DateTime.Now.Hour == 0 && DateTime.Now.Minute == 0 && DateTime.Now.Second > 2)
            {
                playerCountToday = 0;
                foreach (Connection c in Connection.connections)
                {
                    if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName != null && c.myState >= 10)
                        playerCountToday++;
                }
            }

            // See Connection.ProbeLivenessAsync: this is the equivalent of ew-too's angel.c
            // heartbeat - proof the game loop (not just this timer) is actually making
            // progress, not just that the process hasn't exited. Only pet systemd's
            // watchdog when the probe succeeds; if it keeps failing, WatchdogSec (see the
            // sample unit file) will eventually have systemd kill and restart the process,
            // the same outcome angel.c reached when its own heartbeat went quiet.
            LastLivenessOk = await Connection.ProbeLivenessAsync(TimeSpan.FromSeconds(2));
            LastLivenessCheck = DateTime.Now;

            if (LastLivenessOk)
                SdNotify.Watchdog();
        }

        /// <summary>
        /// Closes the current listening socket; RunAsync's loop immediately re-opens a
        /// fresh one on the (possibly changed) configured port. Connected players are
        /// not disconnected - only new-connection acceptance is interrupted briefly.
        /// </summary>
        public static void Restart()
        {
            Console.WriteLine("Restarting server");
            listenerCts?.Cancel();
        }

        public static void Shutdown(int seconds)
        {
            shutdownSecs = seconds;
        }

        public static void cmdUse(string command)
        {
            for (int i = 0; i < commandStats.Count; i++)
            {
                if (commandStats[i].cmd == command)
                {
                    cmdStats c = commandStats[i];
                    c.count++;
                    commandStats[i] = c;
                    return;
                }
            }
            cmdStats add = new cmdStats();
            add.cmd = command;
            add.count = 1;
            commandStats.Add(add);
        }

        public static int cmdUseCount(string command)
        {
            foreach (cmdStats c in commandStats)
            {
                if (c.cmd == command)
                    return c.count;
            }
            return 0;
        }
    }
}
