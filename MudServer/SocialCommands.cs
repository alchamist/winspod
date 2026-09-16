using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using System.Collections.Generic;

namespace MudServer
{
    public partial class Connection
    {

        #region Social stuff

        public void cmdCreateSoc(string message)
        {
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                if (message == "" || split.Length != 2)
                    sendToUser("Syntax: createsoc <name> <type>  (type is simple, complex or private)", true, false, false);
                else
                {
                    string name = split[0];
                    SocialType type;

                    if (!Enum.TryParse<SocialType>(split[1], true, out type))
                        sendToUser("Type must be one of: simple, complex, private", true, false, false);
                    else if (name.Length < 3)
                        sendToUser("Social names must be at least 3 characters long", true, false, false);
                    else if (!char.IsLetter(name[0]))
                        sendToUser("Social names must begin with a letter", true, false, false);
                    else if (name.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '~'))
                        sendToUser("Social names may only contain letters, numbers, and _ - ~", true, false, false);
                    else
                    {
                        foreach (commands c in cmds)
                        {
                            if (c.cmdText.ToLower() == name.ToLower())
                            {
                                sendToUser("There's already a command called \"" + name + "\"", true, false, false);
                                return;
                            }
                        }

                        playerSocials = loadSocials();
                        foreach (social s in playerSocials)
                        {
                            if (!s.Deleted && s.Name.ToLower() == name.ToLower())
                            {
                                sendToUser("A social called \"" + name + "\" already exists", true, false, false);
                                return;
                            }
                        }

                        social newSoc = new social();
                        newSoc.Name = name;
                        newSoc.Creator = myPlayer.UserName;
                        newSoc.Created = DateTime.Now;
                        newSoc.Type = type;

                        playerSocials.Add(newSoc);
                        saveSocials();
                        sendToUser("Social \"" + newSoc.Name + "\" created as a " + type.ToString().ToLower() + " social. Use setsoc to fill in its messages.", true, false, false);
                    }
                }
            }
        }

        public void cmdSetSoc(string message)
        {
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 3);
                if (message == "" || split.Length < 2)
                    sendToUser("Syntax: setsoc <name> <field> <text>\r\nFields: room, self, roomtarget, selftarget, needstarget, type", true, false, false);
                else
                {
                    playerSocials = loadSocials();
                    int matchIndex = -1;
                    for (int i = 0; i < playerSocials.Count; i++)
                    {
                        if (!playerSocials[i].Deleted && playerSocials[i].Name.ToLower() == split[0].ToLower())
                        {
                            matchIndex = i;
                            break;
                        }
                    }

                    if (matchIndex == -1)
                        sendToUser("Social \"" + split[0] + "\" not found", true, false, false);
                    else
                    {
                        social temp = playerSocials[matchIndex];
                        string value = split.Length < 3 ? "" : split[2];

                        switch (split[1].ToLower())
                        {
                            case "room":
                                temp.Messages.ToRoom = value;
                                sendToUser("You " + (value == "" ? "remove" : "set") + " the room message for social \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "self":
                                temp.Messages.ToSelf = value;
                                sendToUser("You " + (value == "" ? "remove" : "set") + " the self message for social \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "roomtarget":
                                temp.Messages.ToRoomTargeted = value;
                                sendToUser("You " + (value == "" ? "remove" : "set") + " the targeted room message for social \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "selftarget":
                                temp.Messages.ToSelfTargeted = value;
                                sendToUser("You " + (value == "" ? "remove" : "set") + " the targeted self message for social \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "needstarget":
                                temp.Messages.NeedsTarget = value;
                                sendToUser("You " + (value == "" ? "remove" : "set") + " the \"needs a target\" message for social \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "type":
                                SocialType type;
                                if (value != "" && Enum.TryParse<SocialType>(value, true, out type))
                                {
                                    temp.Type = type;
                                    sendToUser("You set the type for social \"" + temp.Name + "\" to " + type.ToString().ToLower(), true, false, false);
                                }
                                else
                                {
                                    sendToUser("Type must be one of: simple, complex, private", true, false, false);
                                    return;
                                }
                                break;
                            default:
                                sendToUser("Syntax: setsoc <name> <field> <text>\r\nFields: room, self, roomtarget, selftarget, needstarget, type", true, false, false);
                                return;
                        }

                        playerSocials[matchIndex] = temp;
                        saveSocials();
                    }
                }
            }
        }

        public void cmdDelSoc(string message)
        {
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else if (message == "")
                sendToUser("Syntax: delsoc <name>", true, false, false);
            else
            {
                playerSocials = loadSocials();
                for (int i = playerSocials.Count - 1; i >= 0; i--)
                {
                    if (!playerSocials[i].Deleted && playerSocials[i].Name.ToLower() == message.ToLower())
                    {
                        if (playerSocials[i].Creator.ToLower() == myPlayer.UserName.ToLower() || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                        {
                            social temp = playerSocials[i];
                            temp.Deleted = true;
                            playerSocials[i] = temp;
                            sendToUser("Social \"" + message + "\" deleted", true, false, false);
                            saveSocials();
                        }
                        else
                        {
                            sendToUser("You are not the creator of that social!", true, false, false);
                        }
                        return;
                    }
                }
                sendToUser("Social \"" + message + "\" not found", true, false, false);
            }
        }

        public void cmdXs(string message)
        {
            if (message == "")
                sendToUser("Syntax: xs <social name>", true, false, false);
            else
            {
                playerSocials = loadSocials();
                foreach (social s in playerSocials)
                {
                    if (!s.Deleted && s.Name.ToLower() == message.ToLower())
                    {
                        string output = headerLine("Social: " + s.Name) + "\r\n";
                        output += "^BType: ^N" + s.Type.ToString().PadRight(14) + "^BCreator: ^N" + s.Creator.PadRight(15) + "^BCreated: ^N" + s.Created.ToShortDateString();
                        output += "\r\n" + footerLine() + "\r\n";
                        output += "^PTo room (no target): ^N" + s.Messages.ToRoom + "\r\n";
                        output += "^PTo self (no target): ^N" + s.Messages.ToSelf + "\r\n";
                        output += "^PTo room (targeted): ^N" + s.Messages.ToRoomTargeted + "\r\n";
                        output += "^PTo self (targeted): ^N" + s.Messages.ToSelfTargeted + "\r\n";
                        if (s.Type == SocialType.Private)
                            output += "^PNeeds a target: ^N" + s.Messages.NeedsTarget + "\r\n";
                        output += footerLine();
                        sendToUser(output, true, false, false);
                        return;
                    }
                }
                sendToUser("Social \"" + message + "\" not found", true, false, false);
            }
        }

        // Checked from ProcessLine's main dispatch once no entry in cmdList.dat matches -
        // a social's name isn't a compiled-in command, it's data, so it needs its own
        // lookup rather than going through the MethodInfo.Invoke path the real commands use.
        private bool tryRunSocial(string firstWord, string message, bool adminIdle, bool noAlias)
        {
            playerSocials = loadSocials();
            foreach (social s in playerSocials)
            {
                if (!s.Deleted && s.Name.ToLower() == firstWord.ToLower())
                {
                    if (myPlayer.Away && !adminIdle)
                    {
                        myPlayer.Away = false;
                        sendToUser("You set yourself as back", true, false, false);
                    }

                    runSocial(s, message);

                    Server.cmdUse(s.Name);
                    if (!adminIdle)
                    {
                        myPlayer.LastActive = DateTime.Now;
                        idleHistory.Add(DateTime.Now);
                    }
                    if (!noAlias && myState == 10)
                        doPrompt();

                    return true;
                }
            }
            return false;
        }

        private void runSocial(social soc, string message)
        {
            string targetArg = message.Trim();

            if (targetArg == "")
            {
                if (soc.Type == SocialType.Private)
                {
                    sendToUser(" " + aliasText(soc.Messages.NeedsTarget), true, false, false);
                    return;
                }
                sendToRoom(myPlayer.ColourUserName + " " + aliasText(soc.Messages.ToRoom), "You " + aliasText(soc.Messages.ToSelf));
                return;
            }

            string[] target = matchPartial(targetArg);
            if (target.Length == 0)
                sendToUser("Player \"" + targetArg + "\" not found", true, false, false);
            else if (target.Length > 1)
                sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
            else if (!isOnline(target[0]))
                sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
            else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                sendToUser("You can't target yourself with that!", true, false, false);
            else
            {
                Connection targConn = null;
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        targConn = c;
                }

                if (targConn == null || targConn.myPlayer.UserRoom.ToLower() != myPlayer.UserRoom.ToLower())
                    sendToUser(target[0] + " is not in the same room as you", true, false, false);
                else
                {
                    // Random {a|b|c} phrasing is resolved once so the same pick is used in
                    // both the room and self messages, then %s is swapped for the target's
                    // name - simpler than Playground+'s pipe/pronoun engine (no "you" vs.
                    // name substitution per-recipient), but keeps the same field shape.
                    string toRoom = aliasText(soc.Messages.ToRoomTargeted).Replace("%s", targConn.myPlayer.ColourUserName);
                    string toSelf = aliasText(soc.Messages.ToSelfTargeted).Replace("%s", targConn.myPlayer.ColourUserName);

                    sendToRoom(myPlayer.ColourUserName + " " + toRoom, "You " + toSelf);
                }
            }
        }

        public void saveSocials()
        {
            try
            {
                string path = Path.Combine(Server.userFilePath, @"socials" + Path.DirectorySeparatorChar);
                string fname = "socials.xml";
                string fpath = path + fname;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                // See loadObjects()'s equivalent comment (ObjectCommands.cs) - same
                // caching approach, same reasoning.
                cachedSocialList = playerSocials;

                XmlSerializer serial = new XmlSerializer(typeof(List<social>));
                TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                serial.Serialize(textWriter, playerSocials);
                textWriter.Close();
            }
            catch (Exception ex)
            {
                Connection.logError(ex.ToString(), "filesystem");
            }
        }

        // Loaded once per server run - see loadObjects()'s comment (ObjectCommands.cs) for
        // why this is safe. This one mattered even more than objects: tryRunSocial calls
        // loadSocials() on *every* unmatched command, not just social ones, since it's the
        // dispatch fallback - every mistyped command used to re-read this file from disk.
        private static List<social> cachedSocialList = null;

        public List<social> loadSocials()
        {
            if (cachedSocialList == null)
            {
                List<social> load = new List<social>();
                string path = Path.Combine(Server.userFilePath, @"socials" + Path.DirectorySeparatorChar);
                string fname = "socials.xml";
                string fpath = path + fname;

                if (Directory.Exists(path) && File.Exists(fpath))
                {
                    try
                    {
                        XmlSerializer deserial = new XmlSerializer(typeof(List<social>));
                        TextReader textReader = new StreamReader(@fpath);
                        load = (List<social>)deserial.Deserialize(textReader);
                        textReader.Close();
                    }
                    catch (Exception e)
                    {
                        Debug.Print(e.ToString());
                    }
                }
                cachedSocialList = load;
            }
            return cachedSocialList;
        }

        public static void ClearSocialCache()
        {
            cachedSocialList = null;
        }

        #endregion

    }
}
