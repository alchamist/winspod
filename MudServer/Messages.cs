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

        public void cmdMessage(string message)
        {
            messages = loadMessages();

            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: message <player" + (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? "/all/allstaff/admin/staff/guide" : "") + "> <message>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string[] target = matchPartial(split[0]);

                if (target.Length == 0 && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                    sendToUser("No such user \"" + split[0] + "\"", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target.Length == 1 && (target[0].ToLower() == myPlayer.UserName.ToLower()))
                    sendToUser("Sending a message to yourself, eh?", true, false, false);
                else if (target.Length == 1 && isOnline(target[0]))
                    sendToUser(target[0] + " is online at the moment", true, false, false);
                else
                {
                    List<Player> recipients = new List<Player>();
                    switch (split[0].ToLower())
                    {
                        case "all":
                            recipients = getPlayers(false, false, false, false);
                            break;
                        case "allstaff":
                            recipients = getPlayers(true, false, false, false);
                            break;
                        case "guide":
                            recipients = getPlayers((int)Player.Rank.Guide, true);
                            break;
                        case "staff":
                            recipients = getPlayers((int)Player.Rank.Staff, true);
                            break;
                        case "admin":
                            recipients = getPlayers((int)Player.Rank.Admin, false);
                            break;
                        default:
                            if (target.Length == 0)
                            {
                                recipients = null;
                                sendToUser("No such user \"" + split[0] + "\"", true, false, false);
                            }
                            else
                                recipients.Add(Player.LoadPlayer(target[0], 0));
                            break;
                    }

                    if (recipients != null)
                    {
                        foreach (Player to in recipients)
                        {
                            message m = new message();
                            m.To = to.UserName;
                            m.From = myPlayer.UserName;
                            m.Date = DateTime.Now;
                            m.Body = split[1];
                            m.Deleted = false;
                            m.Read = false;
                            m.Warning = false;
                            messages.Add(m);
                            saveMessages();
                        }
                        sendToUser("Message saved for " + split[0], true, false, false);
                    }
                }
            }
        }

        public void listMessages()
        {
            string mList = "";

            foreach (message m in messages)
            {
                if (m.To == myPlayer.UserName && !m.Warning && !m.Deleted)
                    mList += "{bold}{blue}   From:{reset} " + m.From + "\r\n{bold}{blue}   Date:{reset} " + m.Date.ToShortDateString() + "\r\n{bold}{blue}Message:{reset} " + m.Body + "\r\n\r\n";
            }
            if (mList != "")
                sendToUser("{bold}{cyan}---[{red}Messages{cyan}]".PadRight(103, ' ') + "{reset}\r\n" + mList + "{bold}{cyan}".PadRight(96, ' '), true, false, false);
        }

        public void saveMessages()
        {
            try
            {
                string path = Path.Combine(Server.userFilePath, @"messages" + Path.DirectorySeparatorChar);
                string fname = "messages.xml";
                string fpath = path + fname;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                XmlSerializer serial = new XmlSerializer(typeof(List<message>));
                TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                serial.Serialize(textWriter, messages);
                textWriter.Close();
            }
            catch (Exception ex)
            {
                Connection.logError(ex.ToString(), "filesystem");
            }
        }



        public List<message> loadMessages()
        {
            List<message> load = new List<message>();
            string path = Path.Combine(Server.userFilePath, @"messages" + Path.DirectorySeparatorChar);
            string fname = "messages.xml";
            string fpath = path + fname;

            if (Directory.Exists(path) && File.Exists(fpath))
            {
                try
                {
                    XmlSerializer deserial = new XmlSerializer(typeof(List<message>));
                    TextReader textReader = new StreamReader(@fpath);
                    load = (List<message>)deserial.Deserialize(textReader);
                    textReader.Close();
                }
                catch (Exception e)
                {
                    Debug.Print(e.ToString());
                }
            }
            return load;
        }

    }
}
