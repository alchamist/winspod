using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using System.Collections.Generic;

namespace MudServer
{
    public partial class Connection
    {

        #region Comms commands

        public void cmdSay(string message)
        {
            if (message == "")
                sendToUser("Syntax: say <message>", true, false, false);
            else
                sendToRoom(myPlayer.ColourUserName + " " + sayWord(message, false) + " \"" + wibbleText(message, false) + "{reset}\"", "You " + sayWord(message, true) + " \"" + wibbleText(message, false) + "{reset}\"", myPlayer.UserRoom, myPlayer.UserName, true, false, true);
        }

        public void cmdThink(string message)
        {
            if (message == "")
                sendToUser("Syntax: think <message>", true, false, false);
            else
                sendToRoom(myPlayer.ColourUserName + " thinks . o O ( " + wibbleText(message, false) + " {reset})", "You think . o O ( " + wibbleText(message, false) + " {reset})", false, true);
        }

        public void cmdSing(string message)
        {
            if (message == "")
                sendToUser("Syntax: sing <lyrics>", true, false, false);
            else
                sendToRoom(myPlayer.ColourUserName + " sings o/~ " + wibbleText(message, false) + " {reset}o/~", "You sing o/~ " + wibbleText(message, false) + " {reset}o/~", false, true);
        }

        public void cmdTell(string message)
        {
            if (message.IndexOf(" ") < 0)
                sendToUser("Syntax: \"tell <playername> <message>", true, false, false);
            else
            {
                string target = message.Substring(0, message.IndexOf(" ")).Trim();
                string text = message.Substring(message.IndexOf(" ")).Trim();

                if (target.Length < 2)
                    sendToUser("Please supply at least 2 letters of the target username", true, false, false);
                else
                {
                    string[] matches = matchPartial(target);
                    if (matches.Length == 0)
                        sendToUser("Target \"" + target + "\" not found", true, false, false);
                    else if (matches.Length > 1)
                        sendToUser("Multiple matches found: " + matches.ToString() + " - Please use more letters", true, false, false);
                    else if (matches[0] == myPlayer.UserName)
                        sendToUser("Trying to talk to yourself, eh?", true, false, false);
                    else if (myPlayer.UserRoom.ToLower() == "jail" && myPlayer.JailedUntil > DateTime.Now)
                        sendToUser("Nobody can hear you from your jail cell", true, false, false);
                    else
                    {
                        bool found = false;
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer != null && c.myPlayer.UserName.ToLower() == matches[0].ToLower() && !c.myPlayer.Invisible)
                            {
                                if (c.myPlayer.InEditor)
                                    sendToUser(c.myPlayer.ColourUserName + " is editing at the moment and can't be disturbed", true, false, false);
                                else
                                {
                                    sendToUser("You " + tellWord(text) + c.myPlayer.ColourUserName + " \"" + wibbleText(text, false) + "{reset}\"", true, false);
                                    c.sendToUser("\r\n^Y>>" + myPlayer.ColourUserName + " ^Y" + tellWord(text, false) + "\"" + wibbleText(text, false) + "^Y\"{reset}", true, true, true);
                                    found = true;
                                }
                            }
                        }
                        if (found == false)
                        {
                            sendToUser("User \"" + matches[0] + "\" is not online at the moment", true, true, false);
                        }
                    }
                }
            }
        }

        public void cmdRemote(string message)
        {
            if (message.IndexOf(" ") < 0)
                sendToUser("Syntax: \"remote <playername> <message>", true, false, false);
            else
            {
                string target = message.Substring(0, message.IndexOf(" ")).Trim();
                string text = message.Substring(message.IndexOf(" ")).Trim();

                if (target.Length < 2)
                    sendToUser("Please supply at least 2 letters of the target username", true, false, false);
                else
                {
                    string[] matches = matchPartial(target);
                    if (matches.Length == 0)
                        sendToUser("Target \"" + target + "\" not found", true, false, false);
                    else if (matches.Length > 1)
                        sendToUser("Multiple matches found: " + matches.ToString() + " - Please use more letters", true, false, false);
                    else if (matches[0] == myPlayer.UserName)
                        sendToUser("Trying to emote to yourself, eh?", true, false, false);
                    else if (myPlayer.UserRoom.ToLower() == "jail" && myPlayer.JailedUntil > DateTime.Now)
                        sendToUser("Nobody can hear you from your jail cell", true, false, false);
                    else
                    {
                        bool found = false;
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer != null && c.myPlayer.UserName == matches[0] && !c.myPlayer.Invisible)
                            {
                                if (c.myPlayer.InEditor)
                                    sendToUser(c.myPlayer.ColourUserName + " is editing at the moment and can't be disturbed", true, false, false);
                                else
                                {
                                    if (!text.StartsWith("'"))
                                        text = " " + text;

                                    sendToUser("You emote \"" + myPlayer.ColourUserName + wibbleText(text, true) + "{reset}\" to " + c.myPlayer.ColourUserName, true, false, true);
                                    c.sendToUser("\r\n^Y>>" + myPlayer.ColourUserName + "^Y" + wibbleText(text, true) + "{reset}", true, true, true);
                                }
                                found = true;
                            }
                        }
                        if (found == false)
                        {
                            sendToUser("User \"" + matches[0] + "\" is not online at the moment", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdRSing(string message)
        {
            if (message.IndexOf(" ") < 0)
                sendToUser("Syntax: \"rsing <playername> <message>", true, false, false);
            else
            {
                string target = message.Substring(0, message.IndexOf(" ")).Trim();
                string text = message.Substring(message.IndexOf(" ")).Trim();

                if (target.Length < 2)
                    sendToUser("Please supply at least 2 letters of the target username", true, false, false);
                else
                {
                    string[] matches = matchPartial(target);
                    if (matches.Length == 0)
                        sendToUser("Target \"" + target + "\" not found", true, false, false);
                    else if (matches.Length > 1)
                        sendToUser("Multiple matches found: " + matches.ToString() + " - Please use more letters", true, false, false);
                    else if (matches[0] == myPlayer.UserName)
                        sendToUser("Trying to sing to yourself, eh?", true, false, false);
                    else if (myPlayer.UserRoom.ToLower() == "jail" && myPlayer.JailedUntil > DateTime.Now)
                        sendToUser("Nobody can hear you from your jail cell", true, false, false);
                    else
                    {
                        bool found = false;
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer != null && c.myPlayer.UserName == matches[0] && !c.myPlayer.Invisible)
                            {
                                if (c.myPlayer.InEditor)
                                    sendToUser(c.myPlayer.ColourUserName + " is editing at the moment and can't be disturbed", true, false, false);
                                else
                                {
                                    sendToUser("You sing o/~ " + wibbleText(text, true) + " {reset}o/~ to " + c.myPlayer.ColourUserName, true, false);
                                    c.sendToUser("\r\n^Y>>" + myPlayer.ColourUserName + " ^Ysings o/~ " + wibbleText(text, true) + " ^Yo/~ to you{reset}", true, true, true);
                                }

                                found = true;
                            }
                        }
                        if (found == false)
                        {
                            sendToUser("User \"" + matches[0] + "\" is not online at the moment", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdRThink(string message)
        {
            if (message.IndexOf(" ") < 0)
                sendToUser("Syntax: \"rthink <playername> <message>", true, false, false);
            else
            {
                string target = message.Substring(0, message.IndexOf(" ")).Trim();
                string text = message.Substring(message.IndexOf(" ")).Trim();

                if (target.Length < 2)
                    sendToUser("Please supply at least 2 letters of the target username", true, false, false);
                else
                {
                    string[] matches = matchPartial(target);
                    if (matches.Length == 0)
                        sendToUser("Target \"" + target + "\" not found", true, false, false);
                    else if (matches.Length > 1)
                        sendToUser("Multiple matches found: " + matches.ToString() + " - Please use more letters", true, false, false);
                    else if (matches[0] == myPlayer.UserName)
                        sendToUser("Trying to think to yourself, eh?", true, false, false);
                    else if (myPlayer.UserRoom.ToLower() == "jail" && myPlayer.JailedUntil > DateTime.Now)
                        sendToUser("Nobody can hear you from your jail cell", true, false, false);
                    else
                    {
                        bool found = false;
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer != null && c.myPlayer.UserName == matches[0] && !c.myPlayer.Invisible)
                            {
                                if (c.myPlayer.InEditor)
                                    sendToUser(c.myPlayer.ColourUserName + " is editing at the moment and can't be disturbed", true, false, false);
                                else
                                {
                                    sendToUser("You think . o O ( " + wibbleText(text, true) + " {reset}) to " + c.myPlayer.ColourUserName, true, false);
                                    c.sendToUser("\r\n^Y>>" + myPlayer.ColourUserName + " ^Ythinks . o O ( " + wibbleText(text, true) + " ^Y) to you{reset}", true, true, true);
                                }

                                found = true;
                            }
                        }
                        if (found == false)
                        {
                            sendToUser("User \"" + matches[0] + "\" is not online at the moment", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdEmote(string message)
        {
            if (message == "")
                sendToUser("Syntax: emote <action>", true, false, false);
            else
            {
                if (!message.StartsWith("'"))
                    message = " " + message;
                sendToRoom(myPlayer.ColourUserName + wibbleText(message, true), "You emote: " + myPlayer.ColourUserName + wibbleText(message, true), false, true);
            }
        }

        public void cmdEcho(string message)
        {
            if (message == "")
                sendToUser("Syntax: echo <message>", true, false, false);
            else
            {
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null && !c.myPlayer.InEditor)
                    {
                        if (myPlayer.PlayerRank >= (int)Player.Rank.Admin || !c.myPlayer.SeeEcho)
                        {
                            //sendToUser(wibbleText(message, false), c.myPlayer.UserName, true, c.myPlayer.DoColour, c.myPlayer.UserName == myPlayer.UserName ? false : true, true);
                            c.sendToUser(wibbleText(message, false), true, false, false);
                        }
                        else
                        {
                            sendToUser("{bold}{yellow}[" + myPlayer.ColourUserName + "^Y]{reset} " + wibbleText(message, false), c.myPlayer.UserName, true, c.myPlayer.DoColour, c.myPlayer.UserName == myPlayer.UserName ? false : true, true);
                        }
                    }
                }
            }
        }

        public void cmdShowEcho(string message)
        {
            sendToUser(myPlayer.SeeEcho ? "You will no longer see who echos" : "You will now see who echos", true, false, false);
            myPlayer.SeeEcho = !myPlayer.SeeEcho;
            myPlayer.SavePlayer();
        }

        public void cmdShout(string message)
        {
            if (message == "")
                sendToUser("Syntax: shout <message>", true, false, false);
            else
            {
                if (myPlayer != null)
                {
                    if (myPlayer.UserRoom.ToLower() == "jail" && myPlayer.JailedUntil > DateTime.Now)
                        sendToUser("Nobody can hear you from your jail cell", true, false, false);
                    else if (myPlayer.CanShout)
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer != null && c.myPlayer.UserName != myPlayer.UserName)
                            {
                                if (c.myPlayer.HearShouts && !c.myPlayer.InEditor)
                                    sendToUser(myPlayer.ColourUserName + " shouts \"" + wibbleText(message, false) + "{reset}\"", c.myPlayer.UserName);
                            }
                        }
                        sendToUser("You shout \"" + wibbleText(message, false) + "{reset}\"");
                    }
                    else
                    {
                        sendToUser("You find that your throat is sore and you cannot shout");
                    }
                }
            }
        }

        public void cmdExclude(string message)
        {
            string[] split = message.Split(new char[]{' '},2);
            if (message == "" || split.Length < 2)
                sendToUser("Syntax: exclude <player> <message>", true, false, false);

            else
            {
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("Player \"" + target + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0] == myPlayer.UserName)
                    sendToUser("Trying to exclude yourself, eh?", true, false, false);
                else if (Player.LoadPlayer(target[0], 0).UserRoom != myPlayer.UserRoom)
                    sendToUser(target[0] + " is not in the same room as you ...", true, false, false);
                else
                {
                    sendToRoomExcept(target[0], myPlayer.ColourUserName + " " + sayWord(split[1], true) + " to everone except " + target[0] + " \"" + split[1] + "\"", "You " + sayWord(split[1], true) + " to everone except " + target[0] + " \"" + split[1] + "\"", myPlayer.ColourUserName + " tells everyone in the room something about you", myPlayer.UserRoom, myPlayer.UserName, true, false, false);
                }
            }
        }

        public void cmdTellFriends(string message)
        {
            if (message == "")
                sendToUser("Syntax: tf <message>", true, false, false);
            else if (myPlayer.UserRoom.ToLower() == "jail" && myPlayer.JailedUntil > DateTime.Now)
                sendToUser("Nobody can hear you from your jail cell", true, false, false);
            else
            {
                int count = 0;
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null && myPlayer.isFriend(c.myPlayer.UserName))
                    {
                        count++;
                        if (!c.myPlayer.InEditor)
                            c.sendToUser("\r\n{bold}{green}(To friends) " + myPlayer.ColourUserName + " {bold}{green}" + sayWord(message, false) + " \"" + message + "\"", true, true, true);
                    }
                }
                if (count == 0)
                    sendToUser("None of your friends are online right now");
                else
                    sendToUser("You " + sayWord(message, true) + " to your friends \"" + message + "\"", true, false, true);
            }
        }

        public void cmdEmoteFriends(string message)
        {
            if (message == "")
                sendToUser("Syntax: rf <message>", true, false, false);
            else if (myPlayer.UserRoom.ToLower() == "jail" && myPlayer.JailedUntil > DateTime.Now)
                sendToUser("Nobody can hear you from your jail cell", true, false, false);
            else
            {
                int count = 0;
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null && myPlayer.isFriend(c.myPlayer.UserName))
                    {
                        count++;
                        if (!c.myPlayer.InEditor)
                            c.sendToUser("\r\n{bold}{green}(To friends) " + myPlayer.ColourUserName + "{bold}{green}" + (message.Substring(0, 1) == "'" ? "" : " ") + message, true, true, true);
                    }
                }
                if (count == 0)
                    sendToUser("None of your friends are online right now");
                else
                    sendToUser("You emote to your friends: " + myPlayer.ColourUserName + (message.Substring(0, 1) == "'" ? "" : " ") + message, true, false, true);
            }
        }

        public void cmdTellToFriends(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: ttf <player> <message>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("No such player \"" + message + "\"", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online at the moment", true, false, false);
                else if (myPlayer.UserRoom.ToLower() == "jail" && myPlayer.JailedUntil > DateTime.Now)
                    sendToUser("Nobody can hear you from your jail cell", true, false, false);
                else if (target.Length == 1 && (target[0].ToLower() == myPlayer.UserName.ToLower()))
                    cmdTellFriends(split[1]);
                else
                {
                    Player temp = Player.LoadPlayer(target[0], 0);
                    if (!temp.isFriend(myPlayer.UserName))
                        sendToUser("You are not on " + temp.UserName + "'s friends list", true, false, false);
                    else
                    {
                        int count = 0;
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null)
                            {
                                if (c.myPlayer.UserName != myPlayer.UserName && (temp.isFriend(c.myPlayer.UserName) || c.myPlayer.UserName == temp.UserName))
                                {
                                    count++;
                                    if (!c.myPlayer.InEditor)
                                    {
                                        c.sendToUser("\r\n{bold}{green}(To " + (c.myPlayer.UserName == temp.UserName ? "your" : temp.UserName + "'s") + " friends) " + myPlayer.ColourUserName + " {bold}{green}" + sayWord(split[1], false) + " \"" + split[1] + "\"", true, true, true);
                                    }
                                }
                            }
                        }
                        if (count == 0)
                            sendToUser("None of " + temp.UserName + "'s friends can receive messages at the moment", true, false, false);
                        else
                            sendToUser("You " + sayWord(split[1], true) + " to " + temp.ColourUserName + "'s friends \"" + split[1] + "\"", true, false, true);
                    }
                }
            }
        }

        public void cmdEmoteToFriends(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: rtf <player> <message>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("No such player \"" + message + "\"", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online at the moment", true, false, false);
                else if (myPlayer.UserRoom.ToLower() == "jail" && myPlayer.JailedUntil > DateTime.Now)
                    sendToUser("Nobody can hear you from your jail cell", true, false, false);
                else if (target.Length == 1 && (target[0].ToLower() == myPlayer.UserName.ToLower()))
                    cmdTellFriends(split[1]);
                else
                {
                    Player temp = Player.LoadPlayer(target[0], 0);
                    if (!temp.isFriend(myPlayer.UserName))
                        sendToUser("You are not on " + temp.UserName + "'s friends list", true, false, false);
                    else
                    {
                        int count = 0;
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null)
                            {
                                if (c.myPlayer.UserName != myPlayer.UserName && (temp.isFriend(c.myPlayer.UserName) || c.myPlayer.UserName == temp.UserName))
                                {
                                    count++;
                                    if (!c.myPlayer.InEditor)
                                    {
                                        c.sendToUser("\r\n{bold}{green}(To " + (c.myPlayer.UserName == temp.UserName ? "your" : temp.UserName + "'s") + " friends) " + myPlayer.ColourUserName + "{bold}{green}" + (split[1].Substring(0, 1) == "'" ? "" : " ") + split[1], true, true, true);
                                    }
                                }
                            }
                        }
                        if (count == 0)
                            sendToUser("None of " + temp.UserName + "'s friends can receive messages at the moment", true, false, false);
                        else
                            sendToUser("You emote to " + temp.ColourUserName + "'s friends: " + myPlayer.ColourUserName + (split[1].Substring(0, 1) == "'" ? "" : " ") + split[1], true, false, true);
                    }
                }
            }
        }


        #endregion

    }
}
