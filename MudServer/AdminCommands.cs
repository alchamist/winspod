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

        public void cmdGrant(string message)
        {
            string syntax = "Syntax: Grant <player> <admin/staff/guide/noidle/tester/builder/spod/minister>";
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser(syntax, true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("Player \"" + split[0] + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                //else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                //    sendToUser("You cannot grant privs to yourself!", true, false, false);
                else
                {
                    // Load a temp Player object with the player details, update, then save back
                    // If player is online, need to update their realtime details.

                    Player t = null;
                    bool online = false;

                    if (!isOnline(target[0]))
                    {
                        t = Player.LoadPlayer(target[0], 0);
                    }
                    else
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null)
                            {
                                if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                                {
                                    t = c.myPlayer;
                                    online = true;
                                }
                            }
                        }
                    }

                    if (t == null)
                    {
                        // Something's gone wrong if we're here
                        sendToUser("Strange - something's gone wrong here ...", true, false, false);
                    }
                    else if (t.PlayerRank == (int)Player.Rank.Newbie)
                    {
                        // Can only grant to residents!
                        sendToUser(t.UserName + " needs to be a resident first!", true, false, false);
                    }
                    else
                    {
                        Player.privs p; // for changing player privs;

                        // We have the player object
                        switch (split[1].ToLower())
                        {
                            case "admin":
                                // Granting Admin - no need to do demote staff, as can't demote from HCAdmin
                                if (t.PlayerRank == (int)Player.Rank.Admin)
                                    sendToUser(t.UserName + " is already an admin", true, false, false);
                                else
                                {
                                    sendToStaff(t.UserName + " has just been promoted to Admin by " + myPlayer.UserName, (int)Player.Rank.Admin, true);
                                    logToFile(t.UserName + " has just been promoted to Admin by " + myPlayer.UserName, "grant");
                                    if (online) sendToUser(myPlayer.UserName + " has just promoted you to Admin", t.UserName, true, t.DoColour, false, false);
                                    t.PlayerRank = (int)Player.Rank.Admin;
                                }
                                break;
                            case "staff":
                                // Granting staff
                                if (t.PlayerRank == (int)Player.Rank.Staff)
                                    sendToUser(t.UserName + " is already staff", true, false, false);
                                else
                                {
                                    sendToStaff(t.UserName + " has just been " + (t.PlayerRank > (int)Player.Rank.Staff ? "de" : "pro") + "moted to Staff by " + myPlayer.UserName, (int)Player.Rank.Admin, true);
                                    logToFile(t.UserName + " has just been " + (t.PlayerRank > (int)Player.Rank.Staff ? "de" : "pro") + "moted to Staff by " + myPlayer.UserName, "grant");
                                    if (online) sendToUser(myPlayer.UserName + " has just " + (t.PlayerRank > (int)Player.Rank.Staff ? "de" : "pro") + "moted you to Staff", t.UserName, true, t.DoColour, false, false);
                                    t.PlayerRank = (int)Player.Rank.Staff;
                                }
                                break;
                            case "guide":
                                // Granting guide
                                if (t.PlayerRank == (int)Player.Rank.Guide)
                                    sendToUser(t.UserName + " is already a guide", true, false, false);
                                else
                                {
                                    sendToStaff(t.UserName + " has just been " + (t.PlayerRank > (int)Player.Rank.Guide ? "de" : "pro") + "moted to Guide by " + myPlayer.UserName, (int)Player.Rank.Admin, true);
                                    logToFile(t.UserName + " has just been " + (t.PlayerRank > (int)Player.Rank.Guide ? "de" : "pro") + "moted to Guide by " + myPlayer.UserName, "grant");
                                    if (online) sendToUser(myPlayer.UserName + " has just " + (t.PlayerRank > (int)Player.Rank.Guide ? "de" : "pro") + "moted you to Guide", t.UserName, true, t.DoColour, false, false);
                                    t.PlayerRank = (int)Player.Rank.Guide;
                                }
                                break;
                            case "noidle":
                                // Granting noidle
                                sendToUser("You " + (t.SpecialPrivs.noidle ? "remove" : "grant") + " idle protection to " + t.UserName, true, false, false);
                                if (online) sendToUser(myPlayer.UserName + " has " + (t.SpecialPrivs.noidle ? "removed your" : "granted you") + " idle protection", t.UserName, true, t.DoColour, false, false);
                                logToFile(myPlayer.UserName + " has just " + (t.SpecialPrivs.noidle ? "removed" : "granted") + " idle protection to " + t.UserName, "grant");
                                p = t.SpecialPrivs;
                                p.noidle = !p.noidle;
                                t.SpecialPrivs = p;
                                break;
                            case "builder":
                                // Granting builder
                                sendToUser("You " + (t.SpecialPrivs.builder ? "remove" : "grant") + " builder privs to " + t.UserName, true, false, false);
                                logToFile(myPlayer.UserName + " has just " + (t.SpecialPrivs.builder ? "removed" : "granted") + " builder privs to " + t.UserName, "grant");
                                if (online) sendToUser(myPlayer.UserName + " has " + (t.SpecialPrivs.builder ? "removed your" : "granted you") + " builder privs", t.UserName, true, t.DoColour, false, false);
                                p = t.SpecialPrivs;
                                p.builder = !p.builder;
                                t.SpecialPrivs = p;
                                break;
                            case "tester":
                                // Granting builder
                                sendToUser("You " + (t.SpecialPrivs.tester ? "remove" : "grant") + " tester privs to " + t.UserName, true, false, false);
                                logToFile(myPlayer.UserName + " has just " + (t.SpecialPrivs.tester ? "removed" : "granted") + " tester privs to " + t.UserName, "grant");
                                if (online) sendToUser(myPlayer.UserName + " has " + (t.SpecialPrivs.tester ? "removed your" : "granted you") + " tester privs", t.UserName, true, t.DoColour, false, false);
                                p = t.SpecialPrivs;
                                p.tester = !p.tester;
                                t.SpecialPrivs = p;
                                break;
                            case "spod":
                                // Granting spod
                                sendToUser("You " + (t.SpecialPrivs.spod ? "remove" : "grant") + " spod privs to " + t.UserName, true, false, false);
                                logToFile(myPlayer.UserName + " has just " + (t.SpecialPrivs.spod ? "removed" : "granted") + " spod privs to " + t.UserName, "grant");
                                if (online) sendToUser(myPlayer.UserName + " has " + (t.SpecialPrivs.spod ? "removed your" : "granted you") + " spod privs", t.UserName, true, t.DoColour, false, false);
                                p = t.SpecialPrivs;
                                p.spod = !p.spod;
                                t.SpecialPrivs = p;
                                break;
                            case "minister":
                                // Granting minister
                                sendToUser("You " + (t.SpecialPrivs.minister ? "remove" : "grant") + " minister privs to " + t.UserName, true, false, false);
                                logToFile(myPlayer.UserName + " has just " + (t.SpecialPrivs.minister ? "removed" : "granted") + " minister privs to " + t.UserName, "grant");
                                if (online) sendToUser(myPlayer.UserName + " has " + (t.SpecialPrivs.minister ? "removed your" : "granted you") + " minister privs", t.UserName, true, t.DoColour, false, false);
                                p = t.SpecialPrivs;
                                p.minister = !p.minister;
                                t.SpecialPrivs = p;
                                break;
                            default:
                                sendToUser(syntax, true, false, false);
                                break;
                        }
                        if (online)
                        {
                            // If they are online, update their profile
                            foreach (Connection c in connections)
                            {
                                if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName == t.UserName)
                                {
                                    c.myPlayer = t;
                                    c.myPlayer.SavePlayer();
                                }
                            }
                        }
                        else
                        {
                            t.SavePlayer();
                        }
                    }
                }
            }
        }


        public void cmdRemove(string message)
        {
            if (message == "")
                sendToUser("Syntax: remove <player>", true, false, false);
            else
            {
                string[] target = matchPartial(message);

                if (target.Length == 0)
                    sendToUser("No such user \"" + message + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                {
                    sendToUser("You cannot remove yourself from staff", true, false, false);
                }
                else if (!isOnline(target[0]))
                {
                    Player temp = Player.LoadPlayer(target[0], 0);
                    if (temp == null)
                        sendToUser("Strange ... something somewhere has gone wrong", true, false, false);
                    else
                    {
                        temp.PlayerRank = (int)Player.Rank.Member;
                        sendToStaff(temp.UserName + " has just been removed from the staff by " + myPlayer.UserName, (int)Player.Rank.Admin, true);
                        temp.SavePlayer();
                    }
                }
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            if (c.myPlayer.PlayerRank < (int)Player.Rank.Guide)
                                sendToUser(c.myPlayer.UserName + " isn't on the staff!", true, false, false);
                            else
                            {
                                c.myPlayer.PlayerRank = (int)Player.Rank.Member;
                                sendToStaff(c.myPlayer.UserName + " has just been removed from the staff by " + myPlayer.UserName, (int)Player.Rank.Admin, true);
                                sendToUser("You have just been removed from staff by " + myPlayer.UserName, c.myPlayer.UserName, true, c.myPlayer.DoColour, false, false);
                                c.myPlayer.SavePlayer();
                            }
                        }
                    }
                }
            }
        }

        public void cmdForce(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: Force <player> <command>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("User \"" + split[0] + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("User \"" + target[0] + "\" is not online", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Trying to abuse yourself, eh?", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0])
                        {
                            sendToUser("You force " + target[0] + " to do " + split[1], true, false);
                            c.ProcessLine(split[1]);
                        }
                    }
                }
            }
        }

        public void cmdEdtime(string message)
        {
            if (message == "" || message.IndexOf(" ") < 0 || (message.IndexOf("+") < 0 && message.IndexOf("-") < 0))
                sendToUser("Syntax: Edtime <player> <+/-> <hours>", true, false, false);
            else
            {
                string[] target = matchPartial(message.Substring(0, message.IndexOf(" ")));

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
                    bool increment = message.IndexOf("+") >= 0;
                    int amount = Convert.ToInt16((increment ? message.Substring(message.IndexOf("+") + 1) : message.Substring(message.IndexOf("-") + 1)));
                    int alt = amount;
                    amount = amount * 3600; // get it into hours
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            //c.myPlayer.TotalOnlineTime = increment ? c.myPlayer.TotalOnlineTime + (amount * 3600) : c.myPlayer.TotalOnlineTime - (amount * 3600);
                            //sendToUser("Pre: " + c.myPlayer.TotalOnlineTime.ToString());
                            //sendToUser("Amount: " + amount.ToString());

                            if (increment)
                                c.myPlayer.TotalOnlineTime += amount;
                            else if (!increment && amount >= c.myPlayer.TotalOnlineTime)
                                c.myPlayer.TotalOnlineTime = 0;
                            else
                                c.myPlayer.TotalOnlineTime = c.myPlayer.TotalOnlineTime - amount;

                            c.myPlayer.TrueSpodTime = c.myPlayer.TotalOnlineTime;

                            //sendToUser("Post: " + c.myPlayer.TotalOnlineTime.ToString());

                            sendToUser("You " + (increment ? "add " : "remove ") + alt.ToString() + " hour" + (alt > 1 ? "s " : " ") + (increment ? "to " : "from ") + c.myPlayer.UserName + "'s total time");
                            sendToUser(myPlayer.ColourUserName + " has just altered your total online time", c.myPlayer.UserName);
                            c.myPlayer.SavePlayer();
                        }
                    }
                }

            }
        }

        public void cmdEDump(string message)
        {
            List<Player> playerList = getPlayers((message.ToLower() == "staff"), false, false, false);
            string path = Path.Combine(Server.userFilePath, (@"dump" + Path.DirectorySeparatorChar));
            int count = 0;

            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                FileInfo[] fi = di.GetFiles();
                foreach (FileInfo f in fi)
                {
                    f.Delete();
                }
            }
            else
            {
                Directory.CreateDirectory(path);
            }

            StreamWriter sw = new StreamWriter(path + "emaillist.txt", true);

            foreach (Player p in playerList)
            {
                sw.WriteLine(p.EmailAddress);
                count++;
            }

            sw.Flush();
            sw.Close();
            sendToUser(count.ToString() + " e-mail address" + (count == 1 ? "" : "es") + " dumped to file " + path + "emaillist.txt", true, false, false);
        }

        public void cmdKick(string message)
        {
            if (message == "")
                sendToUser("Syntax: kick <player name> [reason]", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target;
                string reason = "";
                if (split.Length == 1)
                {
                    target = matchPartial(message);
                    reason = "";
                }
                else
                {
                    target = matchPartial(split[0]);
                    reason = split[1];
                }


                if (target.Length == 0)
                    sendToUser("No such user \"" + message.Substring(0, message.IndexOf(" ")) + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Trying to kick yourself?!", true, false, false);
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
                            if (c.myPlayer.PlayerRank > myPlayer.PlayerRank)
                            {
                                sendToUser("Trying to kick a higher rank eh? I think not fluffy puppy!", true, false, false);
                                c.sendToUser("^R" + myPlayer.UserName + " just tried to kick you ... ^N", true, false, false);
                                return;
                            }
                            else
                            {
                                sendToStaff("[" + AppSettings.Default.StaffName.ToUpper() + "] " + c.myPlayer.UserName + " has just been kicked by " + myPlayer.UserName + (reason == "" ? "" : "( " + reason + " )"), (int)Player.Rank.Staff, true);
                                if (reason == "")
                                    c.sendToUser("You must have upset someone as you have just been kicked!", true, false, false);
                                else
                                    c.sendToUser("You have been kicked: " + reason, true, false, false);
                                c.Writer.Flush();

                                c.myPlayer.KickedCount++;
                                c.myPlayer.SavePlayer();
                                c.socket.Close();
                                c.OnDisconnect();

                                logToFile("[Bump] Player \"" + target[0] + "\" has just been bumped by " + myPlayer.UserName, "admin");
                                return;
                            }
                        }
                    }
                }
            }
        }

        public void cmdScare(string message)
        {
            if (message == "")
                sendToUser("Syntax: scare <player name>", true, false, false);
            else
            {

                string[] target = matchPartial(message);

                if (target.Length == 0)
                    sendToUser("No such user \"" + message.Substring(0, message.IndexOf(" ")) + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Trying to scare yourself?!", true, false, false);
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
                            if (c.myPlayer.PlayerRank > myPlayer.PlayerRank)
                            {
                                sendToUser("Trying to scare a higher rank eh? I think not fluffy puppy!", true, false, false);
                                c.sendToUser("^R" + myPlayer.UserName + " just tried to scare you ... ^N", true, false, false);
                                return;
                            }
                            else
                            {
                                string scarefile = AnsiColour.Colorise(loadTextFile(@"files" + Path.DirectorySeparatorChar + "scare.txt"));
                                sendToStaff("[" + AppSettings.Default.AdminName.ToUpper() + "] " + c.myPlayer.UserName + " has just been scared by " + myPlayer.UserName, (int)Player.Rank.Admin, true);
                                c.sendToUser(scarefile, true, false, false);
                                c.Writer.Flush();

                                c.myPlayer.SavePlayer();
                                c.socket.Close();
                                c.OnDisconnect();

                                logToFile("[Scare] Player \"" + target[0] + "\" has just been scared by " + myPlayer.UserName, "admin");
                                return;
                            }
                        }
                    }
                }
            }
        }

        public void cmdKill(string message)
        {
            if (message == "")
                sendToUser("Syntax: kill <player name>", true, false, false);
            else
            {

                string[] target = matchPartial(message);

                if (target.Length == 0)
                    sendToUser("No such user \"" + message.Substring(0, message.IndexOf(" ")) + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Trying to scare yourself?!", true, false, false);
                else
                {
                    if (isOnline(target[0]))
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                            {
                                if (c.myPlayer.PlayerRank > myPlayer.PlayerRank)
                                {
                                    sendToUser("Trying to kill a higher rank eh? I think not fluffy puppy!", true, false, false);
                                    c.sendToUser("^R" + myPlayer.UserName + " just tried to kill you ... ^N", true, false, false);
                                    return;
                                }
                                else
                                {
                                    string scarefile = AnsiColour.Colorise(loadTextFile(@"files" + Path.DirectorySeparatorChar + "kill.txt"));
                                    sendToStaff("[NUKE] " + c.myPlayer.UserName + " has just been killed by " + myPlayer.UserName, (int)Player.Rank.Admin, true);
                                    c.sendToUser(scarefile, true, false, false);
                                    c.Writer.Flush();

                                    c.socket.Close();
                                    c.OnDisconnect();
                                    return;
                                }
                            }
                        }
                    }

                    // Check to see if they were married, if so then make the other person a widow
                    Player p = Player.LoadPlayer(target[0], 0);
                    if (p.Spouse != "" && (p.maritalStatus > Player.MaritalStatus.Single && p.maritalStatus < Player.MaritalStatus.Divorced))
                    {
                        Player t = Player.LoadPlayer(p.Spouse, 0);
                        if (t != null)
                        {
                            t.Spouse = "";
                            t.maritalStatus = Player.MaritalStatus.Widowed;
                            t.SavePlayer();
                            if (isOnline(t.UserName))
                            {
                                foreach(Connection c in connections)
                                {
                                    if (c.myPlayer != null && c.myPlayer.UserName.ToLower() == t.UserName.ToLower())
                                    {
                                        c.myPlayer = t;
                                        c.sendToUser("You have just been made a widow by " + myPlayer.UserName, true, false, false);
                                    }
                                }
                            }
                        }
                    }
                    p = null;

                    // Now need to kill the user file
                    Player.RemovePlayerFile(target[0]);
                    logToFile("[Nuke] Player \"" + target[0] + "\" has just been killed by " + myPlayer.UserName, "admin");
                }
            }
        }

        #region HC Admin commands

        public void cmdShutdown(string message)
        {
            int secs = 0;
            if (message == "")
                sendToUser((Server.shutdownSecs > -1 ? "System set to shutdown at " + DateTime.Now.AddSeconds(Server.shutdownSecs).ToString() : "System shutdown not set"), true, false, false);
            else if (message.ToString() == "abort")
            {
                Server.shutdownSecs = -1;
                sendToUser("Server shutdown aborted", true, false, false);
            }
            else if (!int.TryParse(message, out secs))
                sendToUser("Syntax: shutdown <abort/time in seconds>", true, false, false);
            else
            {
                logToFile("System set to shut down in " + message + " seconds by " + myPlayer.UserName, "admin");
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null)
                    {
                        c.myPlayer.SavePlayer();
                        c.Writer.Write(AnsiColour.Colorise("\r\n" + myPlayer.ColourUserName + " ^Rhas set the system to shutdown in " + formatTime(new TimeSpan(0, 0, secs)) + "!\r\n", c.myPlayer.DoColour));
                        c.Writer.Flush();
                    }
                }
                Server.Shutdown(secs);
            }
        }

        public void cmdRestart(string message)
        {
            logToFile("System restarted by " + myPlayer.UserName, "admin");
            foreach (Connection c in connections)
            {
                try
                {
                    if (c.socket.Connected && c.myPlayer != null)
                    {
                        c.Writer.Write(AnsiColour.Colorise("^RSYSTEM IS RESTARTING - BACK IN A JIFFY!\r\n", c.myPlayer.DoColour));
                        c.Writer.Flush();
                        c.Writer.Close();
                        c.socket.Close();
                    }
                }
                catch
                {

                }
            }
            Server.Restart();
        }

        public void cmdCreatePlayer(string message)
        {
            string[] split = message.Split(' ');
            if (message == "" || split.Length < 2)
            {
                sendToUser("Syntax: pcreate <username> <password> [e-mail address]", true, false, false);
            }
            else if (split.Length == 3 && !testEmailRegex(split[2]))
            {
                sendToUser("Sorry, \"" + split[2] + "\" is not a valid e-mail address", true, false, false);
            }
            else if (NameIsBanned(split[0]) && myPlayer.PlayerRank < (int)Player.Rank.Admin)
            {
                sendToUser("Sorry, that name is banned", true, false, false);
            }
            else if (Regex.Replace(split[0], @"\W*", "") != split[0])
            {
                sendToUser("Sorry, only alphanumerc characters allowed in usernames", true, false, false);
            }
            else if (matchPartial(split[0]).Length > 0)
            {
                sendToUser("Sorry, that name (or similar) is already in use!", true, false, false);
            }
            else
            {
                // Should be good to go
                Player newPlayer = new Player();
                newPlayer.UserName = split[0];
                newPlayer.Password = split[1];
                if (split.Length == 3)
                    newPlayer.EmailAddress = split[2];
                newPlayer.ResBy = myPlayer.UserName;
                newPlayer.ResDate = DateTime.Now;
                newPlayer.Title = "is a newbie, be nice!";
                newPlayer.PlayerRank = (int)Player.Rank.Member;
                newPlayer.SavePlayer();
                myPlayer.ResCount++;
                sendToUser("Player \"" + newPlayer.UserName + "\" successfully created", true, false, false);
            }
        }

        #endregion

    }
}
