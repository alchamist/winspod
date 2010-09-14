using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MudServer
{
    public partial class Connection
    {

        void heartbeat_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (myPlayer != null)
            {
                if (myPlayer.HourlyChime && DateTime.Now.Minute == 0 && DateTime.Now.Hour != lastHChimeHour && !myPlayer.InEditor)
                {
                    lastHChimeHour = DateTime.Now.Hour;
                    sendToUser("{bold}{red}{bell} [[Ding Dong. It is now " + (DateTime.Now.AddHours(myPlayer.JetLag)).ToShortTimeString() + "]{reset}", true, true, false);
                    flushSocket();
                }
                if (myPlayer.PlayerRank < (int)Player.Rank.Admin && !myPlayer.SpecialPrivs.noidle)
                {
                    TimeSpan ts = (TimeSpan)(DateTime.Now - myPlayer.LastActive);
                    if (ts.Seconds == 0 && ts.Minutes >= 20)
                    {
                        if (ts.Minutes == 20)
                        {
                            sendToUser("{bold}{red}You are 20 minutes idle. 10 minutes until auto-boot{reset}");
                            sendToStaff("[Idle] " + myPlayer.UserName + " just received the 20 minute idle warning!", (int)Player.Rank.Staff, true);
                            flushSocket(true);
                        }
                        else if (ts.Minutes == 25)
                        {
                            sendToUser("{bold}{red}You are 25 minutes idle. 5 minutes until auto-boot{reset}");
                            sendToStaff("[Idle] " + myPlayer.UserName + " just received the 25 minute idle warning!", (int)Player.Rank.Staff, true);
                            flushSocket(true);
                        }
                        else if (ts.Minutes == 29)
                        {
                            sendToUser("{bold}{red}You are 29 minutes idle. 1 minute until auto-boot{reset}");
                            sendToStaff("[Idle] " + myPlayer.UserName + " just received the 29 minute idle warning!", (int)Player.Rank.Staff, true);
                            flushSocket(true);
                        }
                        else if (ts.Minutes == 30)
                        {
                            myPlayer.IdledCount++;
                            myPlayer.SavePlayer();
                            sendToUser("{bold}{red}You are 30 minutes idle. Goodbye!{reset}");
                            sendToStaff("[Idle] " + myPlayer.UserName + " has just been auto-booted for idling!", (int)Player.Rank.Staff, true);
                            flushSocket(true);
                            Disconnect();
                        }
                    }
                }
            }

            foreach (Room r in roomList)
            {
                string roomMessage = r.timerFire();
                if (roomMessage != "")
                {
                    sendToRoom("\r\n" + roomMessage, roomMessage, r.systemName, "");
                }

            }

            for (int i = connections.Count - 1; i >= 0; i--)
            {
                Connection c = (Connection)connections[i];
                if (!c.socket.Connected)
                    connections.RemoveAt(i);
            }

            if (Server.shutdownSecs > -1 && myPlayer != null)
            {
                if (Server.shutdownSecs == 3600)
                    sendToUser("^RWarning: ^N&t will shut down in one hour!", true, false, false);
                else if (Server.shutdownSecs == 1800)
                    sendToUser("^RWarning: ^N&t will shut down in 30 minutes!", true, false, false);
                else if (Server.shutdownSecs == 900)
                    sendToUser("^RWarning: ^N&t will shut down in 15 minutes!", true, false, false);
                else if (Server.shutdownSecs == 600)
                    sendToUser("^RWarning: ^N&t will shut down in 10 minutes!", true, false, false);
                else if (Server.shutdownSecs == 300)
                    sendToUser("^RWarning: ^N&t will shut down in 5 minutes!", true, false, false);
                else if (Server.shutdownSecs == 60)
                    sendToUser("^RWarning: ^N&t will shut down in 1 minute!", true, false, false);
                else if (Server.shutdownSecs == 30)
                    sendToUser("^RWarning: ^N&t will shut down in 30 seconds!", true, false, false);
                else if (Server.shutdownSecs == 10)
                    sendToUser("^RWarning: ^N&t will shut down in 10 ...!", true, false, false);
                else if (Server.shutdownSecs == 9)
                    sendToUser("^RWarning: ^N&t will shut down in 9 ...!", true, false, false);
                else if (Server.shutdownSecs == 8)
                    sendToUser("^RWarning: ^N&t will shut down in 8 ...!", true, false, false);
                else if (Server.shutdownSecs == 7)
                    sendToUser("^RWarning: ^N&t will shut down in 7 ...!", true, false, false);
                else if (Server.shutdownSecs == 6)
                    sendToUser("^RWarning: ^N&t will shut down in 6 ...!", true, false, false);
                else if (Server.shutdownSecs == 5)
                    sendToUser("^RWarning: ^N&t will shut down in 5 ...!", true, false, false);
                else if (Server.shutdownSecs == 4)
                    sendToUser("^RWarning: ^N&t will shut down in 4 ...!", true, false, false);
                else if (Server.shutdownSecs == 3)
                    sendToUser("^RWarning: ^N&t will shut down in 3 ...!", true, false, false);
                else if (Server.shutdownSecs == 2)
                    sendToUser("^RWarning: ^N&t will shut down in 2 ...!", true, false, false);
                else if (Server.shutdownSecs == 1)
                    sendToUser("^RWarning: ^N&t will shut down in 1 ...!", true, false, false);
                else if (Server.shutdownSecs == 0)
                {
                    sendToUser("^RWarning: ^N&t is shutting down now ...!", true, false, false);
                }

            }
        }

    }
}
