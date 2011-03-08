using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MudServer
{
    public partial class Connection
    {

        #region sendCommands

        #region sendToAll

        private void sendToAll(string msg)
        {
            sendToAll(msg, true);
        }

        private void sendToAll(string msg, bool newline)
        {
            foreach (Connection conn in connections)
            {
                if (conn.socket.Connected && conn.myPlayer != null && conn.myState >= 10)
                {
                    try
                    {
                        if (conn.myPlayer.CanHear(myPlayer.UserName))
                            conn.Writer.Write(AnsiColour.Colorise(msg, !conn.myPlayer.DoColour));
                    }
                    catch (Exception ex)
                    {
                        logError(ex.ToString(), "Socket write");
                    }
                }
            }
        }

        #endregion

        #region sendToUser

        private void sendToUser(string msg)
        {
            sendToUser(msg, myPlayer.UserName, true, false, false, true);
        }

        private void sendToUser(string msg, bool newline)
        {
            sendToUser(msg, myPlayer.UserName, newline, false, false, true);
        }

        private void sendToUser(string msg, bool newline, bool doPrompt)
        {
            sendToUser(msg, myPlayer.UserName, newline, false, doPrompt, true);
        }

        private void sendToUser(string msg, bool newline, bool doPrompt, bool doHistory)
        {
            sendToUser(msg, myPlayer.UserName, newline, false, doPrompt, doHistory);
        }

        private void sendToUser(string msg, string user)
        {
            sendToUser(msg, user, true, false, false, true);
        }

        private void sendToUser(string msg, string user, bool newline)
        {
            sendToUser(msg, user, newline, false, false, true);
        }

        private void sendToUser(string msg, string user, bool newline, bool removeColour, bool sendPrompt, bool doHistory)
        {
            foreach (Connection conn in connections)
            {
                if (conn.myPlayer != null && conn.myPlayer.UserName.ToLower() == user.ToLower() && msg != null && conn.myPlayer.CanHear(myPlayer.UserName))
                {
                    try
                    {
                        if (conn.socket.Connected)
                        {
                            string prefix = "";
                            if (conn.myPlayer != null && conn.lastSent == conn.myPlayer.Prompt && !msg.StartsWith(conn.myPlayer.Prompt) && conn.myPlayer.UserName != myPlayer.UserName)
                                prefix = "\r\n";
                            if (newline)
                                conn.Writer.WriteLine(prefix + AnsiColour.Colorise(msg, (removeColour || !conn.myPlayer.DoColour)));
                            else
                                conn.Writer.Write(prefix + AnsiColour.Colorise(msg, (removeColour || !conn.myPlayer.DoColour)));

                            conn.Writer.Flush();

                            conn.lastSent = msg;

                            if (doHistory)
                            {
                                conn.history.Add(msg);
                                if (conn.history.Count > 50)
                                    conn.history.RemoveAt(0);
                            }

                            if (sendPrompt)
                                doPrompt(user);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        logError(ex.ToString(), "Socket write");
                    }
                }
            }
        }

        #endregion

        #region sendToRoom

        private void sendToRoom(string msg)
        {
            sendToRoom(msg, msg, myPlayer.UserRoom, myPlayer.UserName, true, true, true);
        }

        private void sendToRoom(string msgToOthers, string msgToSender)
        {
            sendToRoom(msgToOthers, msgToSender, myPlayer.UserRoom, myPlayer.UserName, true, true, true);
        }

        private void sendToRoom(string msgToOthers, string msgToSender, string room, string sender)
        {
            sendToRoom(msgToOthers, msgToSender, room, sender, true, true, true);
        }

        private void sendToRoom(string msgToOthers, string msgToSender, bool senderPrompt, bool receiverPrompt)
        {
            sendToRoom(msgToOthers, msgToSender, myPlayer.UserRoom, myPlayer.UserName, true, senderPrompt, receiverPrompt);
        }

        private void sendToRoom(string msgToOthers, string msgToSender, string room, string sender, bool newline, bool senderPrompt, bool receiverPrompt)
        {
            foreach (Connection conn in connections)
            {
                if (conn.myPlayer != null && conn.myState >= 10 && conn.myPlayer.UserName != sender && conn.myPlayer.UserRoom == room && !conn.myPlayer.InEditor && conn.myPlayer.CanHear(sender))
                {
                    //sendToUser(msgToOthers, conn.myPlayer.UserName, newline, conn.myPlayer.DoColour, receiverPrompt, true);
                    conn.sendToUser(msgToOthers, newline, receiverPrompt, true);
                }
            }
            if (msgToSender != "" && myPlayer != null)
            {
                //sendToUser(msgToSender, sender, newline, myPlayer.DoColour, senderPrompt, true);
                sendToUser(msgToSender, newline, senderPrompt, true);
            }
        }

        private void sendToRoomExcept(string playerToExclude, string msgToOthers, string msgToSender, string msgToExcluded, string room, string sender, bool newline, bool senderPrompt, bool receiverPrompt)
        {
            foreach (Connection conn in connections)
            {
                if (conn.myPlayer != null && conn.myState >= 10 && conn.myPlayer.UserName != "" && conn.myPlayer.UserRoom.ToLower() == room.ToLower())
                {
                    if (conn.myPlayer.UserName.ToLower() == sender.ToLower())
                        conn.sendToUser(msgToSender, newline, senderPrompt, true);
                    else if (conn.myPlayer.UserName.ToLower() == playerToExclude.ToLower())
                        conn.sendToUser(msgToExcluded, newline, receiverPrompt, true);
                    else
                        conn.sendToUser(msgToOthers, newline, receiverPrompt, true);
                }
            }
        }

        #endregion

        #region sendToStaff

        private void sendToStaff(string message, int rank, bool newline)
        {
            foreach (Connection conn in connections)
            {
                if (conn.socket.Connected && conn.myState >= 10 && conn.myPlayer != null && conn.myPlayer.PlayerRank >= rank && myPlayer.onStaffChannel((Player.Rank)rank) && !conn.myPlayer.InEditor && conn.myPlayer.CanHear(myPlayer.UserName))
                {
                    string col = null;
                    switch (rank)
                    {
                        case (int)Player.Rank.Guide:
                            col = AppSettings.Default.GuideColour;
                            break;
                        case (int)Player.Rank.Staff:
                            col = AppSettings.Default.StaffColour;
                            break;
                        case (int)Player.Rank.Admin:
                            col = AppSettings.Default.AdminColour;
                            break;
                        case (int)Player.Rank.HCAdmin:
                            col = AppSettings.Default.HCAdminColour;
                            break;
                    }
                    sendToUser(col + message + "{reset}", conn.myPlayer.UserName, newline);
                }
            }
        }

        #endregion

        #region sendToSpod

        private void sendToSpod(string message)
        {
            foreach (Connection conn in connections)
            {
                if (conn.socket.Connected && conn.myState >= 10 && conn.myPlayer != null && conn.myPlayer.IsSpod && !myPlayer.SpodChannelMute)
                {
                    conn.sendToUser("^c" + message + "{reset}");
                }
            }
        }

        #endregion

        #region sendToChannel

        private void sendToChannel(string channel, string message, bool nohistory)
        {
            ClubChannel chan = ClubChannel.LoadChannel(channel);
            if (chan == null)
                sendToUser("Error sending to channel", true, false, false);
            else
            {
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myState >= 10 && c.myPlayer != null && chan.OnChannel(c.myPlayer.UserName) && !c.myPlayer.ClubChannelMute && !c.myPlayer.InEditor)
                    {
                        c.sendToUser(chan.FormatMessage(message), true, c.myPlayer.UserName != myPlayer.UserName, nohistory);
                    }
                }
            }
        }

        #endregion


        #endregion

    }
}
