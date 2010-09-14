using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MudServer
{
    public partial class Connection
    {

        #region HCAdmin Channel

        public void cmdHU(string message)
        {
            if (message == "")
                sendToUser("Syntax: HU <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.HCAdmin))
                sendToUser("You cannot send to the HCAdmin channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[GOD] " + myPlayer.UserName + " " + sayWord(message, false) + " \"" + message + "\"", (int)Player.Rank.HCAdmin, true);
        }

        public void cmdHT(string message)
        {
            if (message == "")
                sendToUser("Syntax: HT <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.HCAdmin))
                sendToUser("You cannot send to the HCAdmin channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[GOD] " + myPlayer.UserName + " thinks o0o( " + message + " )", (int)Player.Rank.HCAdmin, true);
        }

        public void cmdHS(string message)
        {
            if (message == "")
                sendToUser("Syntax: HS <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.HCAdmin))
                sendToUser("You cannot send to the HCAdmin channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[GOD] " + myPlayer.UserName + " sings ./ " + message + " ./", (int)Player.Rank.HCAdmin, true);
        }

        public void cmdHE(string message)
        {
            if (message == "")
                sendToUser("Syntax: HE <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.HCAdmin))
                sendToUser("You cannot send to the HCAdmin channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[GOD] " + myPlayer.UserName + (message.StartsWith("'") ? "" : " ") + message, (int)Player.Rank.HCAdmin, true);
        }

        public void cmdHM(string message)
        {
            sendToUser("You " + (myPlayer.OnHCAdmin ? "" : "un") + "mute the HCAdmin channel", true, false, false);
            myPlayer.OnHCAdmin = !myPlayer.OnHCAdmin;
        }

        #endregion

        #region Admin Channel

        public void cmdAU(string message)
        {
            if (message == "")
                sendToUser("Syntax: AU <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Admin))
                sendToUser("You cannot send to the admin channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[ADMIN] " + myPlayer.UserName + " " + sayWord(message, false) + " \"" + message + "\"", (int)Player.Rank.Admin, true);
        }

        public void cmdAT(string message)
        {
            if (message == "")
                sendToUser("Syntax: AT <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Admin))
                sendToUser("You cannot send to the admin channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[ADMIN] " + myPlayer.UserName + " thinks o0o( " + message + " )", (int)Player.Rank.Admin, true);
        }

        public void cmdAS(string message)
        {
            if (message == "")
                sendToUser("Syntax: AS <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Admin))
                sendToUser("You cannot send to the admin channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[ADMIN] " + myPlayer.UserName + " sings ./ " + message + " ./", (int)Player.Rank.Admin, true);
        }

        public void cmdAE(string message)
        {
            if (message == "")
                sendToUser("Syntax: AE <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Admin))
                sendToUser("You cannot send to the admin channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[ADMIN] " + myPlayer.UserName + (message.StartsWith("'") ? "" : " ") + message, (int)Player.Rank.Admin, true);
        }

        public void cmdAM(string message)
        {
            sendToUser("You " + (myPlayer.OnAdmin ? "" : "un") + "mute the admin channel", true, false, false);
            myPlayer.OnAdmin = !myPlayer.OnAdmin;
        }

        #endregion

        #region Staff Channel

        public void cmdSU(string message)
        {
            if (message == "")
                sendToUser("Syntax: SU <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Staff))
                sendToUser("You cannot send to the staff channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[STAFF] " + myPlayer.UserName + " " + sayWord(message, false) + " \"" + message + "\"", (int)Player.Rank.Staff, true);
        }

        public void cmdST(string message)
        {
            if (message == "")
                sendToUser("Syntax: ST <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Staff))
                sendToUser("You cannot send to the staff channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[STAFF] " + myPlayer.UserName + " thinks o0o( " + message + " )", (int)Player.Rank.Staff, true);
        }

        public void cmdSS(string message)
        {
            if (message == "")
                sendToUser("Syntax: SS <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Staff))
                sendToUser("You cannot send to the staff channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[STAFF] " + myPlayer.UserName + " sings ./ " + message + " ./", (int)Player.Rank.Staff, true);
        }

        public void cmdSE(string message)
        {
            if (message == "")
                sendToUser("Syntax: SE <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Staff))
                sendToUser("You cannot send to the staff channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[STAFF] " + myPlayer.UserName + (message.StartsWith("'") ? "" : " ") + message, (int)Player.Rank.Staff, true);
        }

        public void cmdSM(string message)
        {
            sendToUser("You " + (myPlayer.OnStaff ? "" : "un") + "mute the staff channel", true, false, false);
            myPlayer.OnStaff = !myPlayer.OnStaff;
        }

        #endregion

        #region Guide Channel

        public void cmdGU(string message)
        {
            if (message == "")
                sendToUser("Syntax: GU <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Guide))
                sendToUser("You cannot send to the guide channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[GUIDE] " + myPlayer.UserName + " " + sayWord(message, false) + " \"" + message + "\"", (int)Player.Rank.Guide, true);
        }

        public void cmdGT(string message)
        {
            if (message == "")
                sendToUser("Syntax: GT <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Guide))
                sendToUser("You cannot send to the guide channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[GUIDE] " + myPlayer.UserName + " thinks o0o( " + message + " )", (int)Player.Rank.Guide, true);
        }

        public void cmdGS(string message)
        {
            if (message == "")
                sendToUser("Syntax: GS <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Guide))
                sendToUser("You cannot send to the guide channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[GUIDE] " + myPlayer.UserName + " sings ./ " + message + " ./", (int)Player.Rank.Guide, true);
        }

        public void cmdGE(string message)
        {
            if (message == "")
                sendToUser("Syntax: GE <message>", true, false, false);
            else if (!myPlayer.onStaffChannel(Player.Rank.Guide))
                sendToUser("You cannot send to the guide channel if you " + (myPlayer.OnDuty ? "have the channel muted" : "are off duty"), true, false, false);
            else
                sendToStaff("[GUIDE] " + myPlayer.UserName + (message.StartsWith("'") ? "" : " ") + message, (int)Player.Rank.Guide, true);
        }

        public void cmdGM(string message)
        {
            sendToUser("You " + (myPlayer.OnGuide ? "" : "un") + "mute the guide channel", true, false, false);
            myPlayer.OnGuide = !myPlayer.OnGuide;
        }

        #endregion

        #region Spod Channel

        public void cmdPU(string message)
        {
            if (message == "")
                sendToUser("Syntax: PU <message>", true, false, false);
            else if (myPlayer.SpodChannelMute || !myPlayer.IsSpod)
                sendToUser("You cannot send to the spod channel if you " + (myPlayer.SpodChannelMute ? "have the channel muted" : "are not a spod"), true, false, false);
            else
                sendToSpod("[spod] " + myPlayer.UserName + " " + sayWord(message, false) + " \"" + message + "\"");
        }

        public void cmdPT(string message)
        {
            if (message == "")
                sendToUser("Syntax: PT <message>", true, false, false);
            else if (myPlayer.SpodChannelMute || !myPlayer.IsSpod)
                sendToUser("You cannot send to the spod channel if you " + (myPlayer.SpodChannelMute ? "have the channel muted" : "are not a spod"), true, false, false);
            else
                sendToSpod("[spod] " + myPlayer.UserName + " thinks o0o( " + message + " )");
        }

        public void cmdPS(string message)
        {
            if (message == "")
                sendToUser("Syntax: PS <message>", true, false, false);
            else if (myPlayer.SpodChannelMute || !myPlayer.IsSpod)
                sendToUser("You cannot send to the spod channel if you " + (myPlayer.SpodChannelMute ? "have the channel muted" : "are not a spod"), true, false, false);
            else
                sendToSpod("[spod] " + myPlayer.UserName + " sings ./ " + message + " ./");
        }

        public void cmdPE(string message)
        {
            if (message == "")
                sendToUser("Syntax: PE <message>", true, false, false);
            else if (myPlayer.SpodChannelMute || !myPlayer.IsSpod)
                sendToUser("You cannot send to the spod channel if you " + (myPlayer.SpodChannelMute ? "have the channel muted" : "are not a spod"), true, false, false);
            else
                sendToSpod("[spod] " + myPlayer.UserName + (message.StartsWith("'") ? "" : " ") + message);
        }

        public void cmdPM(string message)
        {
            sendToUser("You " + (myPlayer.SpodChannelMute ? "un" : "") + "mute the spod channel", true, false, false);
            myPlayer.SpodChannelMute = !myPlayer.SpodChannelMute;
        }

        #endregion


        #region Club Channels Stuff

        public void cmdCClist(string message)
        {
            clubChannels = ClubChannel.LoadAllChannels();

            string ret = "{bold}{cyan}---[{red}Channels{cyan}]".PadRight(103, '-') + "{reset}\r\n";
            if (clubChannels.Count == 0)
                ret += "No channels exist\r\n";
            else
            {
                foreach (ClubChannel c in clubChannels)
                {
                    ret += "{bold}{blue}[" + c.ID + "]{reset} " + c.Name + " - " + c.Description + "\r\n";
                }
            }

            ret += "{bold}{cyan}".PadRight(92, '-') + "{reset}\r\n";
            sendToUser(ret, true, false, false);
        }

        public void cmdCCadd(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: ccadd <channel name> <owner>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target = matchPartial(split[1]);

                if (target.Length == 0)
                    sendToUser("Player \"" + split[1] + "\" not found");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else
                {
                    clubChannels = ClubChannel.LoadAllChannels();

                    bool found = false;
                    foreach (ClubChannel c in clubChannels)
                    {
                        if (c.Name.ToLower() == split[0].ToLower())
                        {
                            found = true;
                        }
                    }

                    if (found)
                        sendToUser("Channel already exists", true, false, false);
                    else
                    {
                        ClubChannel chan = new ClubChannel();
                        chan.Name = split[0];
                        chan.Owner = target[0];
                        clubChannels.Add(chan);
                        ClubChannel.SaveAllChannels(clubChannels, true);
                        sendToUser("Channel \"" + chan.Name + "\" created");
                        logToFile(myPlayer.UserName + " creates a new channel called " + chan.Name + " owned by " + chan.Owner, "channel");
                        if (target[0].ToLower() != myPlayer.UserName.ToLower() && isOnline(target[0]))
                        {
                            Player p = Player.LoadPlayer(target[0], 0);
                            sendToUser("You have been given a new club channel called \"" + chan.Name + "\"", p.UserName, true, p.DoColour, true, false);
                        }
                        clubChannels = ClubChannel.ReindexChannels(clubChannels);
                        clubChannels = ClubChannel.LoadAllChannels();
                    }
                }

            }
        }

        public void cmdCCdel(string message)
        {
            clubChannels = ClubChannel.LoadAllChannels();

            if (message == "")
                sendToUser("Syntax: ccdel <channel name>", true, false, false);
            else
            {
                bool found = false;
                List<ClubChannel> temp = clubChannels;
                for (int i = 0; i < temp.Count; i++)
                {
                    if (temp[i].Name.ToLower() == message.ToLower())
                    {
                        found = true;
                        sendToUser("Channel \"" + temp[i].Name + "\" deleted", true, false, false);
                        logToFile(myPlayer.UserName + " deletes channel \"" + temp[i].Name + "\"", "channel");
                        clubChannels[i].Delete();
                        clubChannels.RemoveAt(i);
                    }
                }
                if (!found)
                {
                    sendToUser("Channel \"" + message + "\" not found", true, false, false);
                }
                ClubChannel.SaveAllChannels(clubChannels, true);
                clubChannels = ClubChannel.LoadAllChannels();
            }
        }

        public void cmdCCrname(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: ccrname <channel name> <new name>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' });
                ClubChannel test = ClubChannel.LoadChannel(split[1]);
                if (test != null)
                {
                    sendToUser("Channel \"" + split[1] + "\" already exists", true, false, false);
                }
                else
                {
                    bool found = false;
                    foreach (ClubChannel c in clubChannels)
                    {
                        if (c.Name.ToLower() == split[0])
                        {
                            found = true;
                            c.Name = split[1];
                            c.SaveChannel();
                            sendToUser("Channel \"" + split[0] + "\" renamed to \"" + split[1] + "\"", true, false, false);
                            logToFile(myPlayer.UserName + " renames channel \"" + split[0] + "\" to \"" + split[1] + "\"", "channel");
                        }
                    }
                    if (!found)
                    {
                        sendToUser("Channel \"" + split[0] + "\" not found", true, false, false);
                    }
                    else
                    {
                        clubChannels = ClubChannel.LoadAllChannels();
                    }
                }
            }
        }

        public void cmdCCinfo(string message)
        {
            if (message == "")
                sendToUser("Syntax: ccinfo <channel>", true, false, false);
            else
            {
                ClubChannel info = ClubChannel.LoadChannel(message);
                if (info == null)
                    sendToUser("Channel \"" + message + "\" not found", true, false, false);
                else
                {
                    string userlist = "";

                    string output = "{bold}{cyan}---[{red}Channel info{cyan}]".PadRight(103, '-') + "{reset}\r\n";
                    output += "{bold}{red} Name: {reset}" + info.PreColour + "[" + info.NameColour + info.Name + info.PreColour + "]{reset}\r\n";
                    output += "{bold}{red}Owner: {reset}" + info.Owner + "\r\n";
                    output += "{bold}{red} Info: {reset}" + info.MainColour + info.Description + "{reset}\r\n";
                    output += "{bold}{red}Users: {reset}";

                    foreach (string u in info.Users)
                    {
                        userlist += ", " + u;
                    }
                    output += (userlist == "" ? "None" : userlist.Substring(2));
                    output += "\r\n{bold}{cyan}".PadRight(92, '-') + "{reset}\r\n";
                    sendToUser(output, true, false, false);
                }
            }
        }

        public void cmdCCjoin(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: ccjoin <channel> <player>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target = matchPartial(split[1]);

                if (target.Length == 0)
                    sendToUser("Player \"" + split[1] + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (ClubChannel.LoadChannel(split[0]) == null)
                    sendToUser("Channel \"" + split[0] + "\" not found", true, false, false);
                else
                {
                    clubChannels = ClubChannel.LoadAllChannels();

                    bool found = false;
                    foreach (ClubChannel c in clubChannels)
                    {
                        if (c.Name.ToLower() == split[0].ToLower())
                        {
                            found = true;
                            if (c.Owner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                                sendToUser("Sorry, you are not the owner of the channel", true, false, false);
                            else if (myPlayer.UserName.ToLower() == target[0].ToLower())
                                sendToUser("You cannot remove yourself from this channel", true, false, false);
                            else
                            {
                                if (c.OnChannel(target[0]))
                                {
                                    sendToUser("Player \"" + target[0] + "\" removed from channel \"" + c.Name + "\"", true, false, false);
                                    if (isOnline(target[0]))
                                        sendToUser("You have been removed from channel \"" + c.Name + "\"", true, true, false);

                                    c.RemovePlayer(target[0]);
                                    c.SaveChannel();
                                }
                                else
                                {
                                    sendToUser("Player \"" + target[0] + "\" added to channel \"" + c.Name + "\"", true, false, false);
                                    if (isOnline(target[0]))
                                        sendToUser("You have been added to channel \"" + c.Name + "\"", true, true, false);

                                    c.AddPlayer(target[0]);
                                    c.SaveChannel();
                                }
                            }

                        }
                    }
                    if (!found)
                        sendToUser("Strange - something has gone a bit wrong", true, false, false);
                    else
                        clubChannels = ClubChannel.LoadAllChannels();
                }
            }
        }

        public void cmdCCdesc(string message)
        {
            if (message == "")
                sendToUser("Syntax: ccdesc <channel> <description>", true, false, false);
            else
            {
                string chanName;
                string chanDesc = "";
                if (message.IndexOf(" ") == -1)
                    chanName = message;
                else
                {
                    string[] split = message.Split(new char[] { ' ' }, 2);
                    chanName = split[0];
                    chanDesc = split[1];
                }


                if (ClubChannel.LoadChannel(chanName) == null)
                    sendToUser("Channel \"" + chanName + "\" not found", true, false, false);
                else
                {
                    clubChannels = ClubChannel.LoadAllChannels();

                    bool found = false;
                    foreach (ClubChannel c in clubChannels)
                    {
                        if (c.Name.ToLower() == chanName.ToLower())
                        {
                            found = true;
                            if (c.Owner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                                sendToUser("Sorry, you are not the owner of the channel", true, false, false);
                            else
                            {
                                c.Description = chanDesc;
                                sendToUser(chanDesc == "" ? "You remove the description for channel \"" + c.Name + "\"" : "You set the description for channel \"" + c.Name + "\" to \"" + chanDesc + "\"", true, false, false);
                                c.SaveChannel();
                            }
                        }
                    }
                    if (!found)
                        sendToUser("Strange ... you shouldn't be here ...", true, false, false);
                    else
                        clubChannels = ClubChannel.LoadAllChannels();
                }
            }
        }

        public void cmdCCpCol(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: ccpcol <channel> <colour code>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);

                if (ClubChannel.LoadChannel(split[0]) == null)
                    sendToUser("Channel \"" + split[0] + "\" not found", true, false, false);
                else
                {
                    clubChannels = ClubChannel.LoadAllChannels();

                    bool found = false;
                    foreach (ClubChannel c in clubChannels)
                    {
                        if (c.Name.ToLower() == split[0].ToLower())
                        {
                            found = true;
                            if (c.Owner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                                sendToUser("Sorry, you are not the owner of the channel", true, false, false);
                            else if (AnsiColour.Colorise(split[1], true) != "")
                                sendToUser("That is not a valid colour code");
                            else
                            {
                                c.PreColour = split[1];
                                sendToUser("Prefix colour set for channel \"" + c.Name + "\"", true, false, false);
                                c.SaveChannel();
                            }
                        }
                    }
                    if (!found)
                        sendToUser("Strange ... you shouldn't be here ...", true, false, false);
                    else
                        clubChannels = ClubChannel.LoadAllChannels();
                }
            }

        }

        public void cmdCCmCol(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: ccmcol <channel> <colour code>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);

                if (ClubChannel.LoadChannel(split[0]) == null)
                    sendToUser("Channel \"" + split[0] + "\" not found", true, false, false);
                else
                {
                    clubChannels = ClubChannel.LoadAllChannels();

                    bool found = false;
                    foreach (ClubChannel c in clubChannels)
                    {
                        if (c.Name.ToLower() == split[0].ToLower())
                        {
                            found = true;
                            if (c.Owner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                                sendToUser("Sorry, you are not the owner of the channel", true, false, false);
                            else if (AnsiColour.Colorise(split[1], true) != "")
                                sendToUser("That is not a valid colour code");
                            else
                            {
                                c.MainColour = split[1];
                                sendToUser("Main colour set for channel \"" + c.Name + "\"", true, false, false);
                                c.SaveChannel();
                            }
                        }
                    }
                    if (!found)
                        sendToUser("Strange ... you shouldn't be here ...", true, false, false);
                    else
                        clubChannels = ClubChannel.LoadAllChannels();
                }
            }

        }

        public void cmdCCnCol(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: ccncol <channel> <colour code>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);

                if (ClubChannel.LoadChannel(split[0]) == null)
                    sendToUser("Channel \"" + split[0] + "\" not found", true, false, false);
                else
                {
                    clubChannels = ClubChannel.LoadAllChannels();

                    bool found = false;
                    foreach (ClubChannel c in clubChannels)
                    {
                        if (c.Name.ToLower() == split[0].ToLower())
                        {
                            found = true;
                            if (c.Owner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                                sendToUser("Sorry, you are not the owner of the channel", true, false, false);
                            else if (AnsiColour.Colorise(split[1], true) != "")
                                sendToUser("That is not a valid colour code");
                            else
                            {
                                c.NameColour = split[1];
                                sendToUser("Name colour set for channel \"" + c.Name + "\"", true, false, false);
                                c.SaveChannel();
                            }
                        }
                    }
                    if (!found)
                        sendToUser("Strange ... you shouldn't be here ...", true, false, false);
                    else
                        clubChannels = ClubChannel.LoadAllChannels();
                }
            }

        }

        public void cmdCCmute(string message)
        {
            myPlayer.ClubChannelMute = !myPlayer.ClubChannelMute;
            sendToUser("You are now " + (myPlayer.ClubChannelMute ? "muting " : "listening to ") + "club channels", true, false, false);
            myPlayer.SavePlayer();
        }

        public void cmdCCwho(string message)
        {
            if (message == "")
                sendToUser("Syntax: ccwho <channel>", true, false, false);
            else
            {
                ClubChannel c = ClubChannel.LoadChannel(message);
                if (c == null)
                    sendToUser("Channel \"" + message + "\" not found", true, false, false);
                else if (!c.OnChannel(myPlayer.UserName) && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                    sendToUser("You are not on channel \"" + c.Name + "\"", true, false, false);
                else
                {
                    string output = "{bold}{cyan}---[{red}CCWho{cyan}]".PadRight(103, '-') + "{reset}\r\nListening on channel \"" + c.Name + "\"\r\n";
                    string users = "";
                    if (isOnline(c.Owner))
                    {
                        foreach (Connection conn in connections)
                        {
                            if (conn.socket.Connected && conn.myPlayer != null && conn.myPlayer.UserName.ToLower() == c.Owner.ToLower() && !conn.myPlayer.ClubChannelMute)
                                users += ", " + conn.myPlayer.ColourUserName;
                        }
                    }
                    foreach (string user in c.Users)
                    {
                        foreach (Connection conn in connections)
                        {
                            if (conn.socket.Connected && conn.myPlayer != null && conn.myPlayer.UserName.ToLower() == user.ToLower() && !conn.myPlayer.ClubChannelMute)
                                users += ", " + conn.myPlayer.ColourUserName;
                        }
                    }

                    sendToUser(output + (users == "" ? "None" : users.Substring(2)) + "\r\n{bold}{cyan}".PadRight(94, '-') + "{reset}", true, false, false);
                }
            }
        }

        public void cmdCCSay(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: cu <channel> <message>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                ClubChannel targ = ClubChannel.LoadChannel(split[0]);
                if (myPlayer.ClubChannelMute)
                    sendToUser("You cannot send to channels if you have them muted", true, false, false);
                else if (targ == null)
                    sendToUser("Channel \"" + split[0] + "\" not found", true, false, false);
                else if (!targ.OnChannel(myPlayer.UserName))
                    sendToUser("You are not on channel \"" + targ.Name + "\"", true, false, false);
                else
                {
                    sendToChannel(targ.Name, myPlayer.UserName + " " + sayWord(split[1], false) + " \"" + split[1] + "\"", false);
                }
            }
        }

        public void cmdCCThink(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: ct <channel> <message>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                ClubChannel targ = ClubChannel.LoadChannel(split[0]);
                if (myPlayer.ClubChannelMute)
                    sendToUser("You cannot send to channels if you have them muted", true, false, false);
                else if (targ == null)
                    sendToUser("Channel \"" + split[0] + "\" not found", true, false, false);
                else if (!targ.OnChannel(myPlayer.UserName))
                    sendToUser("You are not on channel \"" + targ.Name + "\"", true, false, false);
                else
                {
                    sendToChannel(targ.Name, myPlayer.UserName + " thinks . o O ( " + split[1] + " )", false);
                }
            }
        }

        public void cmdCCSing(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: cs <channel> <message>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                ClubChannel targ = ClubChannel.LoadChannel(split[0]);
                if (myPlayer.ClubChannelMute)
                    sendToUser("You cannot send to channels if you have them muted", true, false, false);
                else if (targ == null)
                    sendToUser("Channel \"" + split[0] + "\" not found", true, false, false);
                else if (!targ.OnChannel(myPlayer.UserName))
                    sendToUser("You are not on channel \"" + targ.Name + "\"", true, false, false);
                else
                {
                    sendToChannel(targ.Name, myPlayer.UserName + " sings ./ " + split[1] + " ./", false);
                }
            }
        }

        public void cmdCCEmote(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: ce <channel> <message>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                ClubChannel targ = ClubChannel.LoadChannel(split[0]);
                if (myPlayer.ClubChannelMute)
                    sendToUser("You cannot send to channels if you have them muted", true, false, false);
                else if (targ == null)
                    sendToUser("Channel \"" + split[0] + "\" not found", true, false, false);
                else if (!targ.OnChannel(myPlayer.UserName))
                    sendToUser("You are not on channel \"" + targ.Name + "\"", true, false, false);
                else
                {
                    sendToChannel(targ.Name, myPlayer.UserName + (split[1].Substring(0, 1) == "'" ? "" : " ") + split[1], false);
                }
            }
        }


        private string getChannels(string username)
        {
            clubChannels = ClubChannel.LoadAllChannels();

            string ret = "";
            foreach (ClubChannel c in clubChannels)
            {
                if (c.OnChannel(username))
                    ret += ", " + c.Name;
            }
            return (ret == "" ? "None" : ret.Substring(2));
        }


        #endregion


    }
}
