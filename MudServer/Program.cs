using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Diagnostics;

namespace MudServer
{
    class Server
    {
        static int PortNumber = AppSettings.Default.Port;
        const int BacklogSize = 20;
        public static DateTime startTime = DateTime.Now;

        static void Main(string[] args)
        {
            Version vrs = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Winspod II " + vrs.ToString());
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Initialising");
            int conCount = 0;
            Socket server = new Socket(AddressFamily.InterNetwork,
            SocketType.Stream, ProtocolType.Tcp);
            server.Bind(new IPEndPoint(IPAddress.Any, PortNumber));
            server.Listen(BacklogSize);
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Socket active. Listening for connections on port " + PortNumber.ToString() );
            while (true)
            {
                Socket conn = server.Accept();
                new Connection(conn, conCount++);
            }
        }

        
    }

    
}
