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

        public void cmdOnDuty(string message)
        {
            if (myPlayer.OnDuty)
                sendToUser("You are already on duty", true, false, false);
            else
            {
                myPlayer.OnDuty = true;
                sendToStaff(myPlayer.UserName + " comes on duty", (int)Player.Rank.Guide, true);
                sendToUser("You set yourself on duty", true, false, false);
            }
        }

        public void cmdOffDuty(string message)
        {
            if (!myPlayer.OnDuty)
                sendToUser("You are already off duty", true, false, false);
            else
            {
                sendToStaff(myPlayer.UserName + " goes off duty", (int)Player.Rank.Guide, true);
                myPlayer.OnDuty = false;
                sendToUser("You set yourself off duty");
            }
        }

        public void cmdWall(string message)
        {
            if (message == "")
                sendToUser("Syntax: " + (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? "<*>" : "") + "<message>", true, false, false);
            else if (message.StartsWith("*") && myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null)
                        sendToUser("{bold}{cyan}{bell}>>> {blink}" + myPlayer.UserName + " announces \"" + message.Substring(1).Trim() + "\"{reset}{bold}{cyan} <<{reset}", c.myPlayer.UserName, true, c.myPlayer.DoColour, !(c.myPlayer.UserName == myPlayer.UserName), true);
                }
            }
            else
                sendToRoom("{bold}{white}{bell}>>> " + myPlayer.UserName + " announces \"" + message + "\"{bold}{white} <<{reset}", "{bold}{white}{bell}>>> " + myPlayer.UserName + " announces \"" + message + "\"{bold}{white} <<{reset}", false, true);
        }

        public void cmdWibble(string message)
        {
            if (message == "")
            {
                string output = "";
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null)
                    {
                        if (c.myPlayer.Wibbled)
                            output += c.myPlayer.UserName + " {bold}{blue}(wibbled by " + c.myPlayer.WibbledBy + "){reset}\r\n";
                    }
                }
                if (output == "")
                    sendToUser("No wibbled players on-line", true, false, false);
                else
                {
                    sendToUser("{bold}{cyan}---[{red}Wibbled Players{cyan}]".PadRight(103, '-') + "\r\n{reset}" + output + "{bold}{cyan}".PadRight(92, '-') + "{reset}", true, false, false);
                }
            }
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                {
                    sendToUser("Trying to wibble yourself, eh?", true, false, false);
                    sendToStaff(myPlayer.ColourUserName + " just tried to wibble themselves - what a spanner!", myPlayer.PlayerRank, true);
                }
                else if (!isOnline(target[0]))
                    sendToUser(target[0] + " is not online at the moment", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0])
                        {
                            if (myPlayer.PlayerRank < c.myPlayer.PlayerRank)
                            {
                                sendToUser("Trying to wibble a senior staff member eh?", true, false, false);
                                sendToUser(myPlayer.UserName + " just tried to wibble you!", c.myPlayer.UserName);
                            }
                            else
                            {
                                sendToUser("You have just been " + (c.myPlayer.Wibbled ? "un" : "") + "wibbled by " + myPlayer.UserName, c.myPlayer.UserName);
                                sendToStaff(c.myPlayer.UserName + " has just been " + (c.myPlayer.Wibbled ? "un" : "") + "wibbled by " + myPlayer.UserName, myPlayer.PlayerRank, true);
                                c.myPlayer.WibbledBy = (c.myPlayer.Wibbled ? "" : myPlayer.UserName);
                                c.myPlayer.Wibbled = !c.myPlayer.Wibbled;
                                c.myPlayer.SavePlayer();
                                flushSocket(true);
                            }
                        }
                    }
                }
            }
        }

        public void cmdRes(string message)
        {
            if (message == "")
                sendToUser("Syntax: Res <player>", true, false, false);
            else
            {
                string[] target = matchPartial(message);

                if (target.Length == 0)
                    sendToUser("No such user \"" + message.Substring(0, message.IndexOf(" ")) + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("You cannot grant residency to yourself!", true, false, false);
                else if (!isOnline(target[0]))
                {
                    sendToUser("User \"" + target[0] + "\" is not online", true, false, false);
                }
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            if (c.myPlayer.PlayerRank > (int)Player.Rank.Newbie)
                            {
                                sendToUser(c.myPlayer.UserName + " is already a resident!");
                            }
                            else
                            {
                                sendToUser("You grant residency to " + c.myPlayer.UserName, true, false, false);
                                c.myPlayer.ResBy = myPlayer.UserName;
                                c.myPlayer.ResDate = DateTime.Now;
                                c.myState = 5;
                                sendToUser(myPlayer.ColourUserName + " has granted you residency. Please enter your e-mail address:", c.myPlayer.UserName, true, c.myPlayer.DoColour, false, false);
                            }
                        }
                    }
                }
            }
        }

        public void cmdPBlank(string message)
        {
            if (message == "")
                sendToUser("Syntax: pblank <player>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("No such user \"" + message.Substring(0, message.IndexOf(" ")) + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Trying to blank yourself?", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("User \"" + target[0] + "\" is not online", true, false, false);
                else
                {
                    bool found = false;
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            if (c.myPlayer.PlayerRank >= myPlayer.PlayerRank)
                                sendToUser("Trying to abuse a fellow staff member, eh?");
                            else
                            {
                                found = true;
                                c.myPlayer.Prefix = "";
                                sendToUser("Your prefix has been removed by " + myPlayer.ColourUserName, c.myPlayer.UserName, true, c.myPlayer.DoColour, true, false);
                                sendToUser("You remove " + c.myPlayer.UserName + "'s prefix", true, false, false);
                                c.myPlayer.SavePlayer();
                            }
                        }
                    }
                    if (!found)
                        sendToUser("Strange, something wierd has happened", true, false, false);
                }
            }
        }

        public void cmdTBlank(string message)
        {
            if (message == "")
                sendToUser("Syntax: tblank <player>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("No such user \"" + message.Substring(0, message.IndexOf(" ")) + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Trying to blank yourself?", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("User \"" + target[0] + "\" is not online", true, false, false);
                else
                {
                    bool found = false;
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            if (c.myPlayer.PlayerRank >= myPlayer.PlayerRank)
                                sendToUser("Trying to abuse a fellow staff member, eh?");
                            else
                            {
                                found = true;
                                c.myPlayer.Title = "";
                                sendToUser("Your title has been removed by " + myPlayer.ColourUserName, c.myPlayer.UserName, true, c.myPlayer.DoColour, true, false);
                                sendToUser("You remove " + c.myPlayer.UserName + "'s title", true, false, false);
                                c.myPlayer.SavePlayer();
                            }
                        }
                    }
                    if (!found)
                        sendToUser("Strange, something wierd has happened", true, false, false);
                }
            }
        }

        public void cmdDBlank(string message)
        {
            if (message == "")
                sendToUser("Syntax: dblank <player>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("No such user \"" + message.Substring(0, message.IndexOf(" ")) + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Trying to blank yourself?", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("User \"" + target[0] + "\" is not online", true, false, false);
                else
                {
                    bool found = false;
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            if (c.myPlayer.PlayerRank >= myPlayer.PlayerRank)
                                sendToUser("Trying to abuse a fellow staff member, eh?");
                            else
                            {
                                found = true;
                                c.myPlayer.Title = "";
                                sendToUser("Your title has been removed by " + myPlayer.ColourUserName, c.myPlayer.UserName, true, c.myPlayer.DoColour, true, false);
                                sendToUser("You remove " + c.myPlayer.UserName + "'s title", true, false, false);
                                c.myPlayer.SavePlayer();
                            }
                        }
                    }
                    if (!found)
                        sendToUser("Strange, something wierd has happened", true, false, false);
                }
            }
        }

        public void cmdRename(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: rename <player> <new name>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target = matchPartial(split[0]);
                string[] check = matchPartial(split[1]);
                if (target.Length == 0)
                    sendToUser("Player \"" + split[0] + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Bored of your own name, eh?", true, false, false);
                else if (AnsiColour.Colorise(split[1], true) != split[1])
                    sendToUser("Names cannot contain colour codes or dynamic text", true, false, false);
                else if (check.Length > 0)
                    sendToUser("Username \"" + split[1] + "\" already exists", true, false, false);
                else
                {
                    // We should be good to go
                    Player rename = Player.LoadPlayer(target[0], 0);
                    Player.RemovePlayerFile(target[0]);
                    rename.UserName = split[1];
                    rename.SavePlayer();

                    // Iterate through messages and change To and From as required in messages
                    messages = loadMessages();
                    for (int i = 0; i < messages.Count; i++)
                    {
                        if (messages[i].To == target[0])
                        {
                            message temp = messages[i];
                            temp.To = split[1];
                            messages[i] = temp;
                        }
                        else if (messages[i].From == target[0])
                        {
                            message temp = messages[i];
                            temp.From = split[1];
                            messages[i] = temp;
                        }
                    }
                    saveMessages();

                    // Iterate through the rooms and change owners as necessary
                    roomList = loadRooms();
                    {
                        foreach (Room r in roomList)
                        {
                            Room temp = r;
                            if (temp.roomOwner.ToLower() == target[0])
                            {
                                temp.roomOwner = split[1];
                            }
                            if (temp.systemName.StartsWith(target[0].ToLower() + "."))
                            {
                                string path = Path.Combine(Server.userFilePath, ("rooms" + Path.DirectorySeparatorChar + r.systemName.ToLower() + ".xml"));

                                if (File.Exists(path))
                                {
                                    try
                                    {
                                        File.Delete(path);
                                    }
                                    catch (Exception e)
                                    {
                                        Debug.Print(e.ToString());
                                    }
                                }

                                temp.systemName = temp.systemName.Replace(split[0].ToLower() + ".", split[1].ToLower() + ".");

                            }
                            for (int i = 0; i < temp.exits.Count; i++)
                            {
                                if (temp.exits[i].StartsWith(target[0].ToLower() + "."))
                                {
                                    temp.exits[i] = temp.exits[i].Replace(target[0].ToLower() + ".", target[1].ToLower() + ".");
                                }
                            }
                            temp.SaveRoom();
                        }
                        roomList = loadRooms();
                    }

                    // Check to see if they are married/engaged/whatever
                    if (rename.Spouse != "" && rename.maritalStatus > Player.MaritalStatus.Single)
                    {
                        Player temp = Player.LoadPlayer(rename.Spouse, 0);
                        temp.Spouse = rename.UserName;
                        temp.SavePlayer();
                    }

                    if (isOnline(target[0]))
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                            {
                                // Set rank temporarially to 0 to prevent the Player class destructor saving the old file
                                c.myPlayer = rename;
                            }
                        }
                    }
                    sendToUser("You rename \"" + target[0] + "\" to \"" + split[1] + "\"", true, false, false);
                    logToFile(myPlayer.UserName + " renames \"" + target[0] + "\" to \"" + split[1] + "\"", "admin");
                }
            }
        }

        public void cmdRecap(string message)
        {
            if (message == "")
                sendToUser("Syntax: recap " + (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? "<player> " : "") + "<re-capped name>", true, false, false);
            else
            {
                if (message.IndexOf(" ") == -1)
                {
                    // Are they trying to recap themselves?
                    if (message.ToLower() != myPlayer.UserName.ToLower())
                        sendToUser("Error: name does not match", true, false, false);
                    else
                    {
                        myPlayer.UserName = message;
                        myPlayer.SavePlayer();
                        sendToUser("You recap yourself to " + myPlayer.UserName, true, false, false);
                    }
                }
                else if (myPlayer.PlayerRank < (int)Player.Rank.Admin)
                {
                    sendToUser("Syntax: recap <re-capped name>", true, false, false);
                }
                else
                {
                    string[] split = message.Split(new char[] { ' ' }, 2);
                    string[] target = matchPartial(split[0]);
                    if (target.Length == 0)
                        sendToUser("Player \"" + (message.IndexOf(" ") > -1 ? message.Split(new char[] { ' ' }, 2)[0] : message) + "\" not found", true, false, false);
                    else if (target.Length > 1)
                        sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                    else if (target[0].ToLower() != split[1].ToLower())
                        sendToUser("Error: names do not match", true, false, false);
                    else
                    {
                        if (!isOnline(target[0]))
                        {
                            Player temp = Player.LoadPlayer(target[0], 0);
                            temp.UserName = split[1];
                            temp.SavePlayer();
                            sendToUser("You recap " + target[0] + " to " + temp.UserName, true, false, false);
                        }
                        else
                        {
                            foreach (Connection c in connections)
                            {
                                if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName == target[0])
                                {
                                    c.myPlayer.UserName = split[1];
                                    c.myPlayer.SavePlayer();
                                    sendToUser("You recap " + target[0] + " to " + c.myPlayer.UserName, true, false, false);
                                    c.sendToUser("\r\nYou have been recapped to " + c.myPlayer.UserName + " by " + myPlayer.ColourUserName, true, true, false);
                                }
                            }
                        }
                    }
                }
            }
        }

        public void cmdBump(string message)
        {
            if (message == "")
                sendToUser("Syntax: bump <player name>", true, false, false);
            else
            {
                string[] target = matchPartial(message);

                if (target.Length == 0)
                    sendToUser("No such user \"" + message.Substring(0, message.IndexOf(" ")) + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Trying to bump yourself?!", true, false, false);
                else if (!isOnline(target[0]))
                {
                    sendToUser("User \"" + target[0] + "\" is not online", true, false, false);
                }
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            sendToStaff("[" + AppSettings.Default.AdminName + "] " + c.myPlayer.UserName + " has just been bumped by " + myPlayer.UserName, (int)Player.Rank.Admin, true);
                            c.myPlayer.SavePlayer();
                            c.socket.Close();
                            c.OnDisconnect();
                            return;
                        }
                    }
                }
            }
        }

        public void cmdIdleHistory(string message)
        {
            if (message == "")
            {
                string output = "";
                if (idleHistory.Count == 0)
                    output = "No idlehistory yet\r\n";
                else
                {
                    int count = 0;
                    int start = 0;
                    if (idleHistory.Count > 10)
                        start = idleHistory.Count - 9;
                    int outcount = 1;
                    string time = "";
                    DateTime last = idleHistory[start];
                    foreach (DateTime d in idleHistory)
                    {
                        if (count++ > 3 && count > start)
                        {
                            time = formatTime(TimeSpan.FromSeconds((d - last).Seconds));
                            output += ("^B(" + outcount++.ToString() + ")^N ").PadRight(10) + (time == "" ? "0 seconds" : time) + "\r\n";
                            last = d;
                        }
                    }
                    time = formatTime(TimeSpan.FromSeconds((DateTime.Now - last).Seconds));
                    output += ("^B(" + outcount++.ToString() + ")^N ").PadRight(10) + (time == "" ? "0 seconds" : time) + "\r\n";

                }
                sendToUser(headerLine("IdleHistory for " + myPlayer.UserName) + "\r\n" + (output == "" ? "No idlehistory yet\r\n" : output) + footerLine(), true, false, false);
                return;
            }
            else
            {
                string[] target = matchPartial(message);

                if (target.Length == 0)
                    sendToUser("No such user \"" + message.Substring(0, message.IndexOf(" ")) + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                {
                    sendToUser("User \"" + target[0] + "\" is not online", true, false, false);
                }
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            string output = "";
                            if (c.idleHistory.Count == 0)
                                output = "No idlehistory yet\r\n";
                            else
                            {
                                int count = 0;
                                int start = 0;
                                if (idleHistory.Count > 10)
                                    start = idleHistory.Count - 9;
                                if (c.idleHistory.Count > 10)
                                    count = c.idleHistory.Count - 10;
                                int outcount = 1;
                                string time = "";
                                DateTime last = c.idleHistory[start];
                                foreach (DateTime d in c.idleHistory)
                                {
                                    if (count++ > 3 && count > start)
                                    {
                                        time = formatTime(TimeSpan.FromSeconds((d - last).Seconds));
                                        output += ("^B(" + outcount++.ToString() + ")^N ").PadRight(10) + (time == "" ? "0 seconds" : time) + "\r\n";
                                        last = d;
                                    }
                                }
                                time = formatTime(TimeSpan.FromSeconds((DateTime.Now - last).Seconds));
                                output += ("^B(" + outcount++.ToString() + ")^N ").PadRight(10) + (time == "" ? "0 seconds" : time) + "\r\n";

                            }
                            sendToUser(headerLine("IdleHistory for " + c.myPlayer.UserName) + "\r\n" + (output == "" ? "No idlehistory yet\r\n" : output) + footerLine(), true, false, false);
                            return;
                        }
                    }
                }
            }
        }

        public void cmdLast(string message)
        {
            List<string> last = loadConnectionFile();
            string output = "";
            if (last.Count == 0)
                output = "No previous connections logged\r\n";
            else
            {
                int limit = 0;
                if (last.Count > 20)
                    limit = last.Count - 20;
                int count = 0;
                int outcount = 1;
                foreach (string s in last)
                {
                    if (count++ >= limit)
                    {
                        output += "^B(" + outcount++.ToString().PadLeft(2, '0') + ")^N^g ";
                        if (myPlayer.PlayerRank < (int)Player.Rank.Admin)
                            output += s.Remove(s.IndexOf("|")) + "^N\r\n";
                        else
                            output += s.Replace("|", "") + "^N\r\n";
                    }
                }
            }
            sendToUser(headerLine("Last") + "\r\n" + output + footerLine(), true, false, false);
        }

        public void cmdSlap(string message)
        {
            if (message == "")
                sendToUser("Syntax: slap <player>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else
                {
                    bool found = false;
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            found = true;
                            sendToUser("{bold}{red}{bell} YYou have just been slapped by " + myPlayer.ColourUserName + "{bold}{red}!{reset}", c.myPlayer.UserName, true, c.myPlayer.DoColour, true, true);
                            foreach (Connection conn in connections)
                            {
                                if (conn.socket.Connected && conn.myPlayer != null && conn.myPlayer.UserName != c.myPlayer.UserName)
                                {
                                    sendToUser("{bold}{red}" + c.myPlayer.UserName + " has just been slapped by " + myPlayer.ColourUserName + "{bold}{red}!{reset}", conn.myPlayer.UserName, true, conn.myPlayer.DoColour, !(conn.myPlayer.UserName == myPlayer.UserName), true);
                                }
                            }
                            c.myPlayer.SlappedCount++;
                            c.myPlayer.SavePlayer();
                        }
                    }
                    if (!found)
                    {
                        sendToUser("Player is not online", true, false, false);
                    }
                }
            }
        }

        public void cmdReset(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: Reset <player> <warn/kick/idle/slap>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("Player \"" + split[0] + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online at the moment", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            switch (split[1].Substring(0, 1).ToLower())
                            {
                                case "w":
                                    sendToUser("You reset " + c.myPlayer.UserName + "'s warnings to 0", true, false, false);
                                    c.myPlayer.WarnedCount = 0;
                                    break;
                                case "k":
                                    sendToUser("You reset " + c.myPlayer.UserName + "'s kickings to 0", true, false, false);
                                    c.myPlayer.KickedCount = 0;
                                    break;
                                case "i":
                                    sendToUser("You reset " + c.myPlayer.UserName + "'s idle outs to 0", true, false, false);
                                    c.myPlayer.IdledCount = 0;
                                    break;
                                case "s":
                                    sendToUser("You reset " + c.myPlayer.UserName + "'s slappings to 0", true, false, false);
                                    c.myPlayer.SlappedCount = 0;
                                    break;
                                default:
                                    sendToUser("Syntax: Reset <player> <warn/kick/idle/slap>", true, false, false);
                                    break;
                            }
                            c.myPlayer.SavePlayer();
                        }
                    }
                }
            }
        }

        public void cmdWarn(string message)
        {
            if (message == "" || (message.ToLower() == "list" && myPlayer.PlayerRank < (int)Player.Rank.Admin) || (message.ToLower() != "list" && message.IndexOf(" ") == -1))
                sendToUser("Syntax: warn <player> <warning>", true, false, false);
            else if (message.ToLower() == "list" && myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                string output = "";
                int place = 1;
                foreach (message m in messages)
                {
                    if (m.Warning && !m.Deleted)
                    {
                        output += "{bold}{red}[" + place++.ToString() + "]{blue} From:{reset} " + m.From + " {bold}{blue}To:{reset} " + m.To + "\r\n{bold}{red}Warning:{reset} " + m.Subject + "\r\n";
                    }
                }
                output = "{bold}{cyan}---[{red}Warnings{cyan}]".PadRight(103, '-') + "{reset}\r\n" + (output == "" ? "No warnings" : output) + "\r\n{bold}{cyan}".PadRight(94, '-') + "{reset}";
                sendToUser(output, true, false, false);
            }
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target = matchPartial(split[0]);

                if (target.Length == 0)
                    sendToUser("No such player \"" + split[0] + "\"", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target.Length == 1 && (target[0].ToLower() == myPlayer.UserName.ToLower()))
                    sendToUser("Trying to warn yourself, eh?", true, false, false);
                else
                {
                    bool autoGitted = false;
                    if (isOnline(target[0]))
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName == target[0])
                            {
                                if (c.myPlayer.WarnedCount++ >= 5 && !c.myPlayer.AutoGit && c.myPlayer.PlayerRank < (int)Player.Rank.Admin)
                                {
                                    c.myPlayer.AutoGit = true;
                                    autoGitted = true;
                                }

                                c.sendToUser("{bold}{red}[WARNING] (From: " + myPlayer.ColourUserName + "{bold}{red}) - " + split[1]);
                                sendToStaff("[WARNING] " + myPlayer.UserName + " warns " + c.myPlayer.UserName + " " + split[1] + (autoGitted ? " [Auto Gitted]" : ""), (int)Player.Rank.Staff, true);
                                logToFile("[WARNING] " + myPlayer.UserName + " warns " + c.myPlayer.UserName + " " + split[1] + (autoGitted ? " [Auto Gitted]" : ""), "warning");
                            }
                        }
                    }
                    else
                    {
                        Player temp = Player.LoadPlayer(target[0], 0);
                        messages = loadMessages();

                        if (temp.WarnedCount++ >= 5 && !temp.AutoGit && temp.PlayerRank < (int)Player.Rank.Admin)
                        {
                            temp.AutoGit = true;
                            autoGitted = true;
                        }

                        sendToStaff("[WARNING] " + myPlayer.UserName + " warns " + temp.UserName + " " + split[1] + (autoGitted ? " [Auto Gitted]" : "") + " [Saved]", (int)Player.Rank.Staff, true);
                        logToFile("[WARNING] " + myPlayer.UserName + " warns " + temp.UserName + " " + split[1] + (autoGitted ? " [Auto Gitted]" : ""), "warning");
                        message m = new message();
                        m.From = myPlayer.UserName;
                        m.To = temp.UserName;
                        m.Warning = true;
                        m.Subject = split[1];
                        m.Date = DateTime.Now;
                        messages.Add(m);
                        saveMessages();
                        temp.SavePlayer();
                    }

                }
            }
        }

        public void cmdAGit(string message)
        {
            if (message == "" || message.IndexOf(' ') > -1)
                sendToUser("Syntax: agit <player>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("No such player \"" + message + "\"", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target.Length == 1 && (target[0].ToLower() == myPlayer.UserName.ToLower()))
                    sendToUser("You're a spanner, not a git!", true, false, false);
                else
                {
                    if (isOnline(target[0]))
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName == target[0])
                            {
                                if (!c.myPlayer.AutoGit)
                                    sendToUser(c.myPlayer.UserName + " isn't auto-gitted", true, false, false);
                                else
                                {
                                    sendToUser("You remove the AUTOGIT tag from " + c.myPlayer.UserName, true, false, false);
                                    c.myPlayer.AutoGit = false;
                                    c.myPlayer.SavePlayer();
                                }
                            }
                        }
                    }
                    else
                    {
                        Player temp = Player.LoadPlayer(target[0], 0);
                        if (!temp.AutoGit)
                            sendToUser(temp.UserName + " isn't auto-gitted", true, false, false);
                        else
                        {
                            sendToUser("You remove the AUTOGIT tag from " + temp.UserName, true, false, false);
                            temp.AutoGit = false;
                            temp.SavePlayer();
                        }
                    }
                }
            }
        }

        public void cmdGit(string message)
        {
            if (message == "" || message.IndexOf(' ') > -1)
                sendToUser("Syntax: git <player>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("No such player \"" + message + "\"", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target.Length == 1 && (target[0].ToLower() == myPlayer.UserName.ToLower()))
                    sendToUser("You're a spanner, not a git!", true, false, false);
                else
                {
                    if (isOnline(target[0]))
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName == target[0])
                            {
                                if (c.myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                                {
                                    sendToUser("Admin are always gits!", true, false, false);
                                }
                                else
                                {
                                    if (!c.myPlayer.Git)
                                    {
                                        sendToUser("You add a GIT tag to " + c.myPlayer.UserName, true, false, false);
                                        logToFile(myPlayer.UserName + " adds a GIT tag to " + c.myPlayer.UserName, "git");
                                    }
                                    else
                                    {
                                        sendToUser("You remove the GIT tag from " + c.myPlayer.UserName, true, false, false);
                                        logToFile(myPlayer.UserName + " removes the GIT tag from " + c.myPlayer.UserName, "git");
                                    }
                                    c.myPlayer.Git = !c.myPlayer.Git;
                                    c.myPlayer.SavePlayer();
                                }
                            }
                        }
                    }
                    else
                    {
                        Player temp = Player.LoadPlayer(target[0], 0);
                        if (temp.PlayerRank >= (int)Player.Rank.Admin)
                        {
                            sendToUser("Admin are always gits!", true, false, false);
                        }
                        else
                        {
                            if (!temp.AutoGit)
                            {
                                sendToUser("You add a GIT tag to " + temp.UserName, true, false, false);
                                logToFile(myPlayer.UserName + " adds a GIT tag to " + temp.UserName, "git");
                            }
                            else
                            {
                                sendToUser("You remove the GIT tag from " + temp.UserName, true, false, false);
                                logToFile(myPlayer.UserName + " removes the GIT tag from " + temp.UserName, "git");
                            }
                            temp.Git = !temp.Git;
                            temp.SavePlayer();
                        }
                    }
                }
            }
        }

        public void doWarnings()
        {
            messages = loadMessages();
            string output = "";

            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].To == myPlayer.UserName && messages[i].Warning && !messages[i].Deleted)
                {
                    message temp = messages[i];
                    temp.Deleted = true;
                    output += "{bold}{red}   From:{reset} " + temp.From + "\r\n{bold}{red}Message:{reset} " + temp.Subject + "\r\n";
                    messages[i] = temp;
                }
            }
            if (output != "")
            {
                saveMessages();
                sendToUser("{bold}{cyan}---[{red}Warnings{cyan}]".PadRight(103, '-') + "\r\n" + output + "\r\n{bold}{cyan}".PadRight(94, '-') + "{reset}", true, false, false);
            }
        }

        public void cmdSilence(string message)
        {
            if (message == "")
                sendToUser("Syntax: Silence <player>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("No such user \"" + message + "\"", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Trying to shut yourself up eh?", true, false, false);
                else
                {
                    bool found = false;
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            found = true;
                            if (c.myPlayer.PlayerRank > myPlayer.PlayerRank)
                            {
                                sendToUser("Trying to silence the admin, eh?", true, false, false);
                                sendToUser(myPlayer.UserName + " just tried to remove your shout privs!", c.myPlayer.UserName);
                            }
                            else
                            {
                                if (c.myPlayer.CanShout)
                                {
                                    sendToUser("You remove shout privs from " + c.myPlayer.UserName);
                                    sendToUser("Your shout privs have been removed by " + myPlayer.ColourUserName);
                                }
                                else
                                {
                                    sendToUser("You restore shout privs to " + c.myPlayer.UserName);
                                    sendToUser("Your shout privs have been restored by " + myPlayer.ColourUserName);
                                }
                                c.myPlayer.CanShout = !c.myPlayer.CanShout;
                                c.myPlayer.SavePlayer();
                            }
                        }
                    }
                    if (!found)
                    {
                        sendToUser(target[0] + " is not online at the moment.", true, false, false);
                    }
                }
            }
        }

        public void cmdMuteList(string message)
        {
            string output = "{bold}{cyan}---[{green}Channel Mute List{cyan}]".PadRight(105, '-') + "{reset}\r\n";
            output += "You are currently {bold}" + (myPlayer.OnDuty ? "{green}on" : "{red}off") + "{reset} duty\r\n";
            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                output += AppSettings.Default.AdminColour + "You are " + ((myPlayer.OnAdmin && myPlayer.OnDuty) ? "" : "not ") + "listening to the admin channel {reset}\r\n";
            if (myPlayer.PlayerRank >= (int)Player.Rank.Staff)
                output += AppSettings.Default.StaffColour + "You are " + ((myPlayer.OnStaff && myPlayer.OnDuty) ? "" : "not ") + "listening to the staff channel {reset}\r\n";
            if (myPlayer.PlayerRank >= (int)Player.Rank.Guide)
                output += AppSettings.Default.GuideColour + "You are " + ((myPlayer.OnGuide && myPlayer.OnDuty) ? "" : "not ") + "listening to the guide channel {reset}\r\n";
            output += "{bold}{cyan}" + "".PadRight(80, '-') + "{reset}";
            sendToUser(output, true, false, false);
        }
    }
}
