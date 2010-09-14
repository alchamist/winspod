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
        public void cmdMail(string message)
        {
            mail = loadMails();
            // Monster mailing system
            if (message == "")
                sendToUser("Syntax: mail <list/read/send/reply/del>", true, false, false);
            else
            {
                string action = (message.IndexOf(" ") > -1 ? (message.Split(new char[] { ' ' }, 2))[0] : message);
                int mailID = 0;
                try
                {
                    mailID = (message.IndexOf(" ") > -1 ? Convert.ToInt32((message.Split(new char[] { ' ' }, 2)[1])) : 0);
                }
                catch (Exception ex)
                {
                    Debug.Print(ex.ToString());
                }

                string body = (message.IndexOf(" ") > -1 ? message.Split(new char[] { ' ' }, 2)[1] : "");

                switch (action.ToLower())
                {
                    case "list":
                        listMail();
                        break;
                    case "read":
                        if (mailID == 0)
                            sendToUser("Syntax: mail read <mail id>", true, false, false);
                        else
                            showMail(mailID);
                        break;
                    case "send":
                        if (body == "" || body.IndexOf(" ") == -1)
                            sendToUser("Syntax: mail send <player> <subject>", true, false, false);
                        else
                        {
                            string[] split = body.Split(new char[] { ' ' }, 2);
                            string[] target = matchPartial(split[0]);
                            if (target.Length == 0)
                                sendToUser("No such player \"" + split[0] + "\"", true, false, false);
                            else if (target.Length > 1)
                                sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                            else if (target.Length == 1 && (target[0].ToLower() == myPlayer.UserName.ToLower()))
                                sendToUser("Sending a message to yourself, eh?", true, false, false);
                            else
                            {
                                Player temp = Player.LoadPlayer(target[0], 0);
                                if (temp.CanMail(myPlayer.UserName))
                                {
                                    myPlayer.InMailEditor = true;
                                    sendToUser("Now entering mail editor. Type \".help\" for a list of editor commands", true, false, false);
                                    editMail = new message();
                                    editMail.From = myPlayer.UserName;
                                    editMail.To = target[0];
                                    editMail.Subject = split[1];
                                    editMail.Date = DateTime.Now;
                                    editMail.Read = false;
                                }
                                else
                                    sendToUser("Sorry, " + target[0] + " is currently blocking mail from you", true, false, false);
                            }
                        }
                        break;
                    case "reply":
                        if (mailID == 0)
                            sendToUser("Syntax: mail reply <mail id>", true, false, false);
                        else
                        {
                            int count = 0;
                            bool found = false;
                            foreach (message m in mail)
                            {
                                if (m.To == myPlayer.UserName && m.Deleted == false)
                                {
                                    if (++count == mailID)
                                    {
                                        found = true;
                                        myPlayer.InMailEditor = true;
                                        sendToUser("Now entering mail editor. Type \".help\" for a list of editor commands", true, false, false);
                                        editMail = new message();
                                        editMail.From = myPlayer.UserName;
                                        editMail.To = m.From;
                                        editMail.Subject = (m.Subject.ToLower().IndexOf("re:") == -1 ? "Re: " : "") + m.Subject;
                                        editMail.Date = DateTime.Now;
                                        editMail.Read = false;
                                    }
                                }
                            }
                            if (!found)
                                sendToUser("No such mail ID \"" + mailID.ToString() + "\"", true, false, false);
                        }
                        break;
                    case "del":
                        if (mailID == 0)
                            sendToUser("Syntax: mail del <mail id>", true, false, false);
                        else
                        {
                            int count = 0;
                            bool found = false;
                            for (int i = 0; i < mail.Count; i++)
                            {
                                message m = mail[i];
                                if (m.To == myPlayer.UserName && m.Deleted == false)
                                {
                                    if (++count == mailID)
                                    {
                                        found = true;
                                        m.Deleted = true;
                                        mail[i] = m;
                                        sendToUser("Mail ID \"" + mailID + "\" deleted", true, false, false);
                                        saveMails();
                                    }
                                }
                            }
                            if (!found)
                                sendToUser("No such mail ID \"" + mailID.ToString() + "\"", true, false, false);
                        }
                        break;
                    default:
                        sendToUser("Syntax: mail <list/read/send/reply/del>", true, false, false);
                        break;

                }
            }
        }

        public void listMail()
        {
            string output = "";
            int count = 1;
            foreach (message m in mail)
            {
                if (m.To == myPlayer.UserName && !m.Deleted)
                {
                    output += " " + (m.Read ? " " : "{bold}{red}*{white}") + " " + count++.ToString() + "{reset}".PadRight(13, ' ') + m.From.PadRight(16, ' ') + m.Subject + "\r\n";
                }
            }
            if (output == "")
                output = "No messages\r\n";
            sendToUser("{bold}{cyan}-[{red} ID {cyan}]--[{red}From{cyan}]----------[{red}Subject{cyan}]" + "{reset}".PadLeft(53, '-') + "\r\n" + output + "{bold}{cyan}".PadRight(92, '-') + "{reset}", true, false, false);
        }

        public void showMail(int mailPlace)
        {
            int count = 0;
            bool found = false;

            for (int i = 0; i < mail.Count; i++)
            {
                if (mail[i].To == myPlayer.UserName && mail[i].Deleted == false)
                {
                    if (++count == mailPlace)
                    {
                        found = true;
                        string output = ("{bold}{cyan}---[{red}Mail: " + mailPlace.ToString() + "{cyan}]").PadRight(103, '-') + "\r\n";
                        message m = mail[i];
                        if (!m.Read)
                            m.Read = true;

                        output += "{bold}{blue}   From: {reset}" + m.From + "\r\n";
                        output += "{bold}{blue}     To: {reset}" + m.To + "\r\n";
                        output += "{bold}{blue}Subject: {reset}" + m.Subject + "\r\n";
                        output += "{bold}{blue}   Date: {reset}" + m.Date.ToShortDateString() + " " + m.Date.ToShortTimeString() + "\r\n";
                        output += "{bold}{cyan}".PadRight(92, '-') + "{reset}\r\n";
                        output += "{bold}{blue}Message:{reset}\r\n" + m.Body + "\r\n";
                        output += "{bold}{cyan}".PadRight(92, '-') + "{reset}\r\n";
                        sendToUser(output);
                        mail[i] = m;
                        saveMails();
                    }
                }
            }

            if (!found)
                sendToUser("No such mail ID \"" + mailPlace.ToString() + "\"", true, false, false);

        }

        public void mailEdit(string message)
        {
            if (message.StartsWith("."))
            {
                switch (message)
                {
                    case ".end":
                    case ".":
                        myPlayer.InMailEditor = false;
                        mail.Add(editMail);
                        if (isOnline(editMail.To))
                        {
                            sendToUser("{bold}{yellow}{bell}YYou have just received a new mail from " + myPlayer.ColourUserName, editMail.To, true, false, true, false);
                        }
                        sendToUser("Mail sent to " + editMail.To, true, false, false);
                        editMail = new message();
                        saveMails();
                        break;
                    case ".wipe":
                        editMail.Body = "";
                        break;
                    case ".view":
                        sendToUser(editMail.Body, true, false, false);
                        break;
                    case ".quit":
                        editMail = new message();
                        myPlayer.InMailEditor = false;
                        sendToUser("Mail aborted", true, false, false);
                        break;
                    default:
                        sendToUser("Commands available:\r\n.view - show current meesage content\r\n.wipe - wipe current message content\r\n.quit - exit the editor without saving message\r\n.end - exit the editor and send message", true, false, false);
                        break;
                }
            }
            else
            {
                editMail.Body += message + "\r\n";
            }
            doPrompt();
        }

        public void saveMails()
        {
            try
            {
                string path = Path.Combine(Server.userFilePath, @"mail" + Path.DirectorySeparatorChar);
                string fname = "mail.xml";
                string fpath = path + fname;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                XmlSerializer serial = new XmlSerializer(typeof(List<message>));
                TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                serial.Serialize(textWriter, mail);
                textWriter.Close();
            }
            catch (Exception ex)
            {
                Connection.logError(ex.ToString(), "filesystem");
            }
        }

        public List<message> loadMails()
        {
            List<message> load = new List<message>();
            string path = Path.Combine(Server.userFilePath, @"mail" + Path.DirectorySeparatorChar);
            string fname = "mail.xml";
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

        public void checkMail()
        {
            mail = loadMails();
            int count = 0;
            foreach (message m in mail)
            {
                if (m.To == myPlayer.UserName && m.Read == false)
                    count++;
            }

            if (count > 0)
                sendToUser("{bold}{yellow}{bell}YYou have " + count.ToString() + " unread mail" + (count > 1 ? "s" : ""), true, false, false);
        }

    }
}
