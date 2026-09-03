using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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

        public static DateTime startTime = DateTime.Now;
        public static Socket server;
        public static int shutdownSecs = -1;
        public static int playerCount = 0;
        public static int playerCountToday = 0;

        public static string userFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winspod");

        public struct cmdStats
        {
            public string cmd;
            public int count;
        }

        public static List<cmdStats> commandStats = new List<cmdStats>();

        static CancellationTokenSource listenerCts;
        static readonly System.Timers.Timer tickTimer = new System.Timers.Timer(1000);

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
            while (!stoppingToken.IsCancellationRequested)
            {
                using (listenerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    await ListenAsync(listenerCts.Token);
                }
            }
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

            int conCount = 0;

            // Closing the socket is what unblocks AcceptAsync below when we're asked to stop.
            using (token.Register(() => { try { server.Close(); } catch { } }))
            {
                try
                {
                    while (true)
                    {
                        Socket conn = await server.AcceptAsync(token);
                        var connection = new Connection(conn, conCount++);
                        _ = connection.RunAsync();
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }
        }

        static void Tick(object sender, ElapsedEventArgs e)
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
