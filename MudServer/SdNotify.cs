using System;
using System.Net.Sockets;
using System.Text;

namespace MudServer
{
    /// <summary>
    /// Minimal implementation of systemd's sd_notify protocol: a single datagram sent
    /// to a Unix domain socket named by the NOTIFY_SOCKET environment variable. No
    /// package needed - the protocol is a handful of lines.
    ///
    /// This is what lets systemd do the one thing a plain `Restart=on-failure` unit
    /// can't: detect a HUNG-but-still-running process via `WatchdogSec=`. That's the
    /// same problem ew-too's angel.c solved with its own bespoke heartbeat socket
    /// (see angel.c's alive_fd/select() loop). Everything else angel.c did - fork,
    /// exec, wait for exit, restart, give up after N crashes in a window - is already
    /// systemd's job via Restart=/StartLimitBurst= in the unit file, so there's no
    /// reason to duplicate a process supervisor here; this only covers the part a
    /// generic supervisor can't see on its own.
    ///
    /// A no-op everywhere systemd isn't in the picture (dev machine, macOS, a plain
    /// `dotnet run`, a container without socket-notify wired up) since NOTIFY_SOCKET
    /// is simply unset there - every call below silently does nothing.
    /// </summary>
    public static class SdNotify
    {
        static readonly string SocketPath = Environment.GetEnvironmentVariable("NOTIFY_SOCKET");
        static Socket socket;

        public static bool IsAvailable => !string.IsNullOrEmpty(SocketPath);

        /// <summary>Tell systemd the service has finished starting (for Type=notify units).</summary>
        public static void Ready() => Send("READY=1");

        /// <summary>"Pet the dog" - tell systemd this process is still making progress.</summary>
        public static void Watchdog() => Send("WATCHDOG=1");

        public static void Stopping() => Send("STOPPING=1");

        public static void Status(string message) => Send("STATUS=" + message);

        static void Send(string state)
        {
            if (!IsAvailable)
                return;

            try
            {
                socket ??= new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);

                // A leading '@' denotes Linux's abstract socket namespace, represented
                // in the actual address as a leading NUL byte instead of '@'.
                string path = SocketPath.StartsWith("@") ? "\0" + SocketPath.Substring(1) : SocketPath;
                var endpoint = new UnixDomainSocketEndPoint(path);

                byte[] data = Encoding.UTF8.GetBytes(state);
                socket.SendTo(data, endpoint);
            }
            catch
            {
                // A notify failure should never take the game server down with it.
            }
        }
    }
}
