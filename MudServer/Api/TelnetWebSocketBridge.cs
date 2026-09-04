using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MudServer.Api
{
    /// <summary>
    /// Bridges a browser WebSocket connection to the existing telnet listener via a plain
    /// loopback TCP connection, rather than teaching Connection.cs (and the ~60 places
    /// across the codebase that check socket.Connected/Close it) about a second transport.
    /// From the game engine's point of view this is just another telnet client connecting
    /// from 127.0.0.1 - it never knows a browser is involved.
    ///
    /// This is also the answer to the Cloudflare Tunnel gap noted in ROADMAP.md: a
    /// WebSocket is an upgraded HTTP connection, so it tunnels through a free Cloudflare
    /// Tunnel with no paid TCP product needed, unlike raw telnet.
    /// </summary>
    public static class TelnetWebSocketBridge
    {
        // The exact 3-byte IAC sequences Connection.cs's sendEchoOff()/sendEchoOn() send
        // (Outputs.cs). A browser has no telnet client to interpret these, so they're
        // translated into a small marker the front end watches for to toggle local echo
        // during password entry, instead of being forwarded as raw bytes it can't use.
        private const string EchoOffMarker = "ECHO-OFF";
        private const string EchoOnMarker = "ECHO-ON";

        public static void Map(WebApplication app)
        {
            app.UseWebSockets();
            app.Map("/ws", HandleAsync);
        }

        static async Task HandleAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();
            using TcpClient tcp = new TcpClient();

            try
            {
                await tcp.ConnectAsync(IPAddress.Loopback, AppSettings.Default.Port);
            }
            catch (Exception)
            {
                await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "Could not reach the game server", CancellationToken.None);
                return;
            }

            NetworkStream stream = tcp.GetStream();
            using CancellationTokenSource cts = new CancellationTokenSource();

            Task toWs = PumpTcpToWebSocketAsync(stream, ws, cts.Token);
            Task toTcp = PumpWebSocketToTcpAsync(ws, stream, cts.Token);

            await Task.WhenAny(toWs, toTcp);
            cts.Cancel();

            try { tcp.Close(); } catch { }
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch { }
        }

        /// <summary>
        /// Reads raw Telnet bytes from the game connection and forwards them to the
        /// browser, stripping Telnet IAC negotiation entirely (a browser doesn't speak
        /// Telnet - see Connection.ReadBoundedLineAsync/SkipTelnetCommandAsync for the
        /// same parsing applied on the game's own inbound side) and translating the
        /// echo-off/on sequences into the marker above.
        /// </summary>
        static async Task PumpTcpToWebSocketAsync(NetworkStream stream, WebSocket ws, CancellationToken token)
        {
            List<byte> pending = new List<byte>();
            byte[] one = new byte[1];

            async Task FlushAsync()
            {
                if (pending.Count == 0)
                    return;
                byte[] payload = pending.ToArray();
                pending.Clear();
                await ws.SendAsync(payload, WebSocketMessageType.Text, true, token);
            }

            async Task SendMarkerAsync(string marker)
            {
                await FlushAsync();
                await ws.SendAsync(Encoding.UTF8.GetBytes(marker), WebSocketMessageType.Text, true, token);
            }

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (await stream.ReadAsync(one, 0, 1, token) == 0)
                        break;
                    byte b = one[0];

                    if (b == 0xFF) // IAC
                    {
                        if (await stream.ReadAsync(one, 0, 1, token) == 0)
                            break;
                        byte command = one[0];

                        if (command == 0xFF)
                        {
                            pending.Add(0xFF); // escaped literal 0xFF data byte
                        }
                        else if (command == 0xFB || command == 0xFC || command == 0xFD || command == 0xFE)
                        {
                            if (await stream.ReadAsync(one, 0, 1, token) == 0)
                                break;
                            byte option = one[0];

                            if (command == 0xFB && option == 0x01) // WILL ECHO
                                await SendMarkerAsync(EchoOffMarker);
                            else if (command == 0xFC && option == 0x01) // WONT ECHO
                                await SendMarkerAsync(EchoOnMarker);
                            // else: any other negotiation - already fully consumed, browser doesn't need it
                        }
                        else if (command == 0xFA) // subnegotiation - discard up to IAC SE
                        {
                            byte prev = 0;
                            while (true)
                            {
                                if (await stream.ReadAsync(one, 0, 1, token) == 0)
                                    return;
                                if (prev == 0xFF && one[0] == 0xF0)
                                    break;
                                prev = one[0];
                            }
                        }
                        // else: other 2-byte commands (NOP/AYT/etc.) already fully consumed
                    }
                    else
                    {
                        pending.Add(b);
                    }

                    if (pending.Count > 0 && (!stream.DataAvailable || pending.Count > 4096))
                        await FlushAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        /// <summary>
        /// Forwards whatever the browser sends straight to the game connection. The front
        /// end is responsible for line-buffering locally and sending a trailing \r\n per
        /// submitted line (see wwwroot/index.html) - the game's own line reader already
        /// handles \r / \n / \r\n the same way regardless of client.
        /// </summary>
        static async Task PumpWebSocketToTcpAsync(WebSocket ws, NetworkStream stream, CancellationToken token)
        {
            byte[] buf = new byte[4096];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await ws.ReceiveAsync(buf, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    if (result.Count > 0)
                        await stream.WriteAsync(buf, 0, result.Count, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }
    }
}
