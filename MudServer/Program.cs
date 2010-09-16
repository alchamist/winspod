using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Timers;
using System.Threading;
using System.Diagnostics;

namespace MudServer
{
    class Server
    {
        static int PortNumber = AppSettings.Default.Port;
        const int BacklogSize = 20;
        public static DateTime startTime = DateTime.Now;
        public static string[] sysargs = null;
        public static Thread socketThread;
        public static Socket server;
        public static System.Timers.Timer t = new System.Timers.Timer();
        public static int shutdownSecs = -1;
        public static int playerCount = 0;

        public static string userFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winspod");  

        public struct cmdStats
        {
            public string cmd;
            public int count;
        }

        public static List<cmdStats> commandStats = new List<cmdStats>();

        static void Main(string[] args)
        {
            t.Interval = 1000;
            t.Elapsed += new ElapsedEventHandler(t_Elapsed);
            t.Start();

            socketThread = new Thread(new ThreadStart(Startup));
            socketThread.Start();

            if (AppSettings.Default.HTTPEnabled && HttpListener.IsSupported || 1 == 1)
            {
                new webserver();
            }

            //sysargs = args;
            //Version vrs = Assembly.GetExecutingAssembly().GetName().Version;
            //Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Winspod II " + vrs.ToString());
            //Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Initialising");
            //int conCount = 0;
            //Socket server = new Socket(AddressFamily.InterNetwork,
            //SocketType.Stream, ProtocolType.Tcp);
            //server.Bind(new IPEndPoint(IPAddress.Any, PortNumber));
            //server.Listen(BacklogSize);
            //Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Socket active. Listening for connections on port " + PortNumber.ToString());
            //while (true)
            //{
            //    Socket conn = server.Accept();
            //    new Connection(conn, conCount++);
            //}
        }

        static void t_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (shutdownSecs > -1)
            {
                if (shutdownSecs-- == 0)
                {
                    t.Stop();
                    server.Close();
                    socketThread.Abort();
                    Thread.Sleep(1);
                    socketThread.Join();
                    Environment.Exit(1);
                }
            }
        }

        static void Startup()
        {
            //sysargs = args;
            Version vrs = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Winspod II " + vrs.ToString());
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Initialising");
            int conCount = 0;
            //Socket 
                server = new Socket(AddressFamily.InterNetwork,
            SocketType.Stream, ProtocolType.Tcp);
            server.Bind(new IPEndPoint(IPAddress.Any, PortNumber));
            server.Listen(BacklogSize);
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Socket active. Listening for connections on port " + PortNumber.ToString());
            while (true)
            {
                Socket conn = server.Accept();
                new Connection(conn, conCount++);
            }
        }

        public static void Restart()
        {
            server.Close();
            socketThread.Abort();

            socketThread.Join();
            Console.WriteLine("Restarting server");
            socketThread = new Thread(new ThreadStart(Startup));
            socketThread.Start();
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
