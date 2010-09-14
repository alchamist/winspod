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

        public List<IPAddress> loadIPBans()
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
            return ret;
        }

        public void saveIPBans()
        {
            try
            {
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

        public List<string> loadNameBans()
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

            return load;
        }

        public void saveNameBans()
        {
            try
            {
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
    }
}
