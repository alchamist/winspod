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

        public void cmdSite(string message)
        {
            if (message == "")
                sendToUser("Syntax: site <player> or site <ip prefix>", true, false, false);
            else
            {
                string prefix;

                if (char.IsDigit(message[0]))
                {
                    // A raw prefix, taken as typed - "site 82.19" matches anyone whose IP
                    // starts with that, no need to pad it out to full octets.
                    prefix = message;
                }
                else
                {
                    string[] target = matchPartial(message);
                    if (target.Length == 0)
                    {
                        sendToUser("Player \"" + message + "\" not found", true, false, false);
                        return;
                    }
                    else if (target.Length > 1)
                    {
                        sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                        return;
                    }

                    Player targ = null;
                    foreach (Player p in getPlayers())
                    {
                        if (p.UserName.ToLower() == target[0].ToLower())
                            targ = p;
                    }

                    if (targ == null || string.IsNullOrEmpty(targ.CurrentIP))
                    {
                        sendToUser(target[0] + " has no known IP address on record", true, false, false);
                        return;
                    }

                    // Drop to the first two octets, same as ew-too's "site <player>" did -
                    // a /16-ish match wide enough to catch a dynamic-IP reconnect, not just
                    // an exact repeat.
                    string[] octets = targ.CurrentIP.Split('.');
                    prefix = octets.Length >= 2 ? octets[0] + "." + octets[1] + "." : targ.CurrentIP;
                }

                string output = "";
                foreach (Player p in getPlayers())
                {
                    if (!string.IsNullOrEmpty(p.CurrentIP) && p.CurrentIP.StartsWith(prefix))
                        output += "\r\n" + p.ColourUserName.PadRight(40 + (p.ColourUserName.Length - p.UserName.Length), ' ') + ": " + p.CurrentIP + (isOnline(p.UserName) ? " (online)" : "");
                }

                sendToUser(headerLine("People from \"" + prefix + "\"") + (output == "" ? "\r\nNo matches found" : output) + "\r\n" + footerLine(), true, false, false);
            }
        }

        public void cmdIpBan(string message)
        {
            IPAddress ban = null;

            if (message == "" || !IPAddress.TryParse(message, out ban))
                sendToUser("Syntax: ipban <ip address>", true, false, false);
            else
            {
                IPBanList = loadIPBans();
                foreach (IPAddress i in IPBanList)
                {
                    if (i == ban)
                    {
                        sendToUser("IP Address " + ban.ToString() + " already in ban list", true, false, false);
                        return;
                    }
                }
                IPBanList.Add(ban);
                saveIPBans();
                sendToUser("IP Address " + ban.ToString() + " added to ban list", true, false, false);
            }
        }

        public void cmdIpUnBan(string message)
        {
            IPAddress ban = null;

            if (message == "" || !IPAddress.TryParse(message, out ban))
                sendToUser("Syntax: ipunban <ip address>", true, false, false);
            else
            {
                IPBanList = loadIPBans();
                for (int i = IPBanList.Count - 1; i >= 0; i--)
                {
                    if (IPBanList[i].Equals(ban))
                    {
                        IPBanList.RemoveAt(i);
                        sendToUser("IP Address " + ban.ToString() + " removed from ban list", true, false, false);
                        saveIPBans();
                        return;
                    }
                }
                sendToUser("IP Address " + ban.ToString() + " not in ban list", true, false, false);
            }
        }

        public void cmdNameBan(string message)
        {
            if (message == "")
                sendToUser("Syntax: nban <name to ban>", true, false, false);
            else if (NameIsBanned(message))
                sendToUser("\"" + message + "\" is already in the ban list", true, false, false);
            else
            {
                NameBanList.Add(message);
                saveNameBans();
                sendToUser("Name \"" + message + "\" added to the ban list", true, false, false);
            }
        }

        public void cmdNameUnBan(string message)
        {
            if (message == "")
                sendToUser("Syntax: nunban <name to unban>", true, false, false);
            else if (!NameIsBanned(message))
                sendToUser("Name \"" + message + "\" is not in the ban list", true, false, false);
            else
            {
                NameBanList = loadNameBans();
                for (int i = NameBanList.Count - 1; i >= 0; i--)
                {
                    if (NameBanList[i].ToLower() == message.ToLower())
                    {
                        NameBanList.RemoveAt(i);
                        saveNameBans();
                        sendToUser("Name \"" + message + "\" removed from the ban list", true, false, false);
                        return;
                    }
                }
                sendToUser("Strange .. you shouldn't be here ...", true, false, false);
            }
        }

        public bool IpIsBanned(IPAddress check)
        {
            IPBanList = loadIPBans();
            return (IPBanList.IndexOf(check) > -1);
        }

        public bool NameIsBanned(string check)
        {
            NameBanList = loadNameBans();
            foreach (string n in NameBanList)
            {
                if (n.ToLower() == check.ToLower())
                    return true;
            }
            return false;
        }

        // See loadObjects()'s comment (ObjectCommands.cs) for the caching approach/why it's
        // safe. IpIsBanned runs on every incoming connection, so this one is on the
        // hottest possible path.
        private static List<IPAddress> cachedIPBanList = null;

        public List<IPAddress> loadIPBans()
        {
            if (cachedIPBanList == null)
            {
                List<string> load = new List<string>();
                string path = Path.Combine(Server.userFilePath, @"banish" + Path.DirectorySeparatorChar);
                string fname = "ipban.xml";
                string fpath = path + fname;

                if (Directory.Exists(path) && File.Exists(fpath))
                {
                    try
                    {
                        XmlSerializer deserial = new XmlSerializer(typeof(List<string>));
                        TextReader textReader = new StreamReader(@fpath);
                        load = (List<string>)deserial.Deserialize(textReader);
                        textReader.Close();
                    }
                    catch (Exception e)
                    {
                        Debug.Print(e.ToString());
                    }
                }
                List<IPAddress> ret = new List<IPAddress>();
                foreach (string s in load)
                {
                    IPAddress test = null;
                    if (IPAddress.TryParse(s, out test))
                        ret.Add(test);
                }
                cachedIPBanList = ret;
            }
            return cachedIPBanList;
        }

        public void saveIPBans()
        {
            try
            {
                cachedIPBanList = IPBanList;

                List<string> output = new List<string>();
                foreach (IPAddress i in IPBanList)
                {
                    output.Add(i.ToString());
                }
                string path = Path.Combine(Server.userFilePath, @"banish" + Path.DirectorySeparatorChar);
                string fname = "ipban.xml";
                string fpath = path + fname;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                XmlSerializer serial = new XmlSerializer(typeof(List<string>));
                TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                serial.Serialize(textWriter, output);
                textWriter.Close();
            }
            catch (Exception ex)
            {
                Connection.logError(ex.ToString(), "filesystem");
            }
        }

        // See loadIPBans()'s comment just above for the caching approach.
        private static List<string> cachedNameBanList = null;

        public List<string> loadNameBans()
        {
            if (cachedNameBanList == null)
            {
                List<string> load = new List<string>();
                string path = Path.Combine(Server.userFilePath, @"banish" + Path.DirectorySeparatorChar);
                string fname = "nameban.xml";
                string fpath = path + fname;

                if (Directory.Exists(path) && File.Exists(fpath))
                {
                    try
                    {
                        XmlSerializer deserial = new XmlSerializer(typeof(List<string>));
                        TextReader textReader = new StreamReader(@fpath);
                        load = (List<string>)deserial.Deserialize(textReader);
                        textReader.Close();
                    }
                    catch (Exception e)
                    {
                        Debug.Print(e.ToString());
                    }
                }
                cachedNameBanList = load;
            }
            return cachedNameBanList;
        }

        public void saveNameBans()
        {
            try
            {
                cachedNameBanList = NameBanList;

                string path = Path.Combine(Server.userFilePath, @"banish" + Path.DirectorySeparatorChar);
                string fname = "nameban.xml";
                string fpath = path + fname;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                XmlSerializer serial = new XmlSerializer(typeof(List<string>));
                TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                serial.Serialize(textWriter, NameBanList);
                textWriter.Close();
            }
            catch (Exception ex)
            {
                Connection.logError(ex.ToString(), "filesystem");
            }
        }

        public static void ClearBanCache()
        {
            cachedIPBanList = null;
            cachedNameBanList = null;
        }
    }
}
