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

        #region fileMethods

        private string loadTextFile(string path)
        {
            if (File.Exists(path))
            {
                TextReader textReader = new StreamReader(path);
                string output = textReader.ReadToEnd();
                textReader.Close();
                return output;
            }
            else
            {
                logError("Unable to load file " + path + " - file does not exist", "File I/O");
                return "";
            }
        }

        private List<string> loadConnectionFile()
        {
            string path = Path.Combine(Server.userFilePath, @"connections" + Path.DirectorySeparatorChar + "connections.log");
            List<string> ret = new List<string>();
            if (File.Exists(path))
            {
                TextReader textRead = new StreamReader(path);
                string line = "";
                while ((line = textRead.ReadLine()) != null)
                {
                    ret.Add(line);
                }
                textRead.Close();
                textRead.Dispose();
            }
            else
            {
                logError("Unable to load file " + path + " - file does not exist", "File I/O");
            }
            return ret;
        }

        private void logConnection(string name, string ip, DateTime time)
        {
            //string path = @"logs" + Path.DirectorySeparatorChar + "connections.log";

            string path = Path.Combine(Server.userFilePath, (@"connections" + Path.DirectorySeparatorChar));
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                StreamWriter sw = new StreamWriter(path + "connections.log", true);
                sw.WriteLine("[" + time.ToString() + "] " + name + " |(" + ip + ")");
                sw.Flush();
                sw.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Error writing to connections log: " + e.ToString());
            }

        }

        private void loadCommands()
        {
            cmds.Clear();
            string path = @"commands" + Path.DirectorySeparatorChar + "cmdList.dat";
            if (File.Exists(path))
            {
                Regex splitRX = new Regex(@",\s*", RegexOptions.Compiled);
                using (StreamReader sr = new StreamReader(path))
                {
                    string line = null;

                    while ((line = sr.ReadLine()) != null)
                    {
                        string[] fields = splitRX.Split(line.Replace("\"", ""));
                        if (fields.Length == 5 && fields[0] == "" && fields[1] == "")
                        {
                            string[] temp = fields;
                            fields = new string[] { ",", temp[2], temp[3], temp[4] };
                        }
                        if (fields.Length == 4)
                        {
                            commands cmd;
                            cmd.cmdText = fields[0].ToString();
                            cmd.cmdCall = fields[1].ToString();
                            cmd.level = Convert.ToInt32(fields[2].ToString());
                            cmd.helpSection = fields[3];

                            if (cmd.level <= myPlayer.PlayerRank && ((cmd.helpSection != "tester" && cmd.helpSection != "builder") || (cmd.helpSection == "tester" && myPlayer.SpecialPrivs.tester) || (cmd.helpSection == "builder" && myPlayer.SpecialPrivs.builder) || myPlayer.PlayerRank >= (int)Player.Rank.Admin))
                                cmds.Add(cmd);
                        }
                    }

                    Debug.Print(cmds.Count.ToString() + " commands loaded");
                }

            }
            else
            {
                logError("Unable to load commands - cmdList.dat missing", "File I/O");
            }
        }

        private List<Room> loadRooms()
        {
            List<Room> list = new List<Room>();
            string path = Path.Combine(Server.userFilePath, (@"rooms" + Path.DirectorySeparatorChar));
            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);

                FileInfo[] fi = di.GetFiles();

                foreach (FileInfo file in fi)
                {
                    Room load = Room.LoadRoom(file.Name.Replace(".xml", ""));
                    if (load != null)
                        list.Add(load);
                }
            }

            if (list.Count == 0 || list == null)
            {
                // There are no rooms, so we need to create a default one!
                Room newRoom = new Room();
                newRoom.systemName = "Main";
                newRoom.fullName = "The default room";
                newRoom.description = "A very basic room. Not much to see here";
                newRoom.systemRoom = true;
                newRoom.roomOwner = "System";
                newRoom.SaveRoom();
                list.Add(newRoom);
            }
            return list;
        }

        public static void logError(string error, string type)
        {
            string sLogFormat = DateTime.Now.ToShortDateString().ToString() + " " + DateTime.Now.ToLongTimeString().ToString() + " = " + type + " ==> " + error;
            try
            {
                string path = Path.Combine(Server.userFilePath, @"logs" + Path.DirectorySeparatorChar);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                StreamWriter sw = new StreamWriter(path + "error.log", true);
                sw.WriteLine(sLogFormat);
                sw.Flush();
                sw.Close();
                Debug.Print(error);
                Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Error caught: " + error);
            }
            catch (Exception e)
            {
                Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Error writing to error log: " + e.ToString());
            }
        }

        public static void logToFile(string logMessage, string logFile)
        {
            string sLogFormat = "[" + DateTime.Now.ToShortDateString().ToString() + " " + DateTime.Now.ToLongTimeString().ToString() + "] " + logMessage;
            string path = Path.Combine(Server.userFilePath, (@"logs" + Path.DirectorySeparatorChar));
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                StreamWriter sw = new StreamWriter(path + logFile + ".log", true);
                sw.WriteLine(sLogFormat);
                sw.Flush();
                sw.Close();
                Console.WriteLine("[" + DateTime.Now.ToShortDateString().ToString() + " " + DateTime.Now.ToShortTimeString() + "] " + logFile + ": " + logMessage);
            }
            catch (Exception e)
            {
                Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Error writing to " + logFile + " log: " + e.ToString());
            }
        }

        public void cmdCheckLogs(string message)
        {
            string path = Path.Combine(Server.userFilePath, (@"logs" + Path.DirectorySeparatorChar));
            string output = "";
            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                FileInfo[] fi = di.GetFiles();

                foreach (FileInfo f in fi)
                {
                    if (f.LastWriteTime > myPlayer.LastLogon)
                        output += ", " + f.Name.Replace(f.Extension, "");
                }
            }
            sendToUser("Log touches: " + (output == "" ? "None" : output.Substring(2)), true, false, false);
        }

        public void cmdMOTD(string message)
        {
            string path = (@"files" + Path.DirectorySeparatorChar);
            string motd = AnsiColour.Colorise(loadTextFile(path + "motd.txt"));
            motd = "{bold}{cyan}---[{red}Message of the Day{cyan}]".PadRight(103, '-') + "\r\n{reset}" + motd;
            sendToUser(motd + "\r\n{bold}{cyan}" + "".PadRight(80, '-') + "{reset}\r\n", true, false, false);
        }

        public void showMOTD(bool force)
        {
            if (myPlayer.LastLogon.DayOfYear < DateTime.Now.DayOfYear || force)
            {
                string path = (@"files" + Path.DirectorySeparatorChar);
                string motd = AnsiColour.Colorise(loadTextFile(path + "motd.txt"));
                string sumotd = AnsiColour.Colorise(loadTextFile(path + "sumotd.txt"));
                motd = "{bold}{cyan}---[{red}Message of the Day{cyan}]".PadRight(103, '-') + "\r\n{reset}" + motd;
                sumotd = "\r\n{bold}{cyan}---[{green}Staff Message of the Day{cyan}]".PadRight(107, '-') + "\r\n{reset}" + sumotd;
                sendToUser(motd + (myPlayer.PlayerRank >= (int)Player.Rank.Guide ? sumotd : "") + "\r\n{bold}{cyan}" + "".PadRight(80, '-') + "{reset}\r\n", true, false, false);
            }
        }

        public void cmdVlog(string message)
        {
            // Routine to view logs
            string path = Path.Combine(Server.userFilePath, (@"logs" + Path.DirectorySeparatorChar));

            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                FileInfo[] fi = di.GetFiles();
                if (message == "")
                {
                    // List all files
                    string fileList = "";
                    foreach (FileInfo f in fi)
                    {
                        fileList += ", " + f.Name.Replace(f.Extension, "");
                    }
                    if (fileList == "")
                        sendToUser("No logs available to view", true, false, false);
                    else
                        sendToUser("Available logs: " + fileList.Substring(2), true, false, false);
                }
                else
                {
                    bool found = false;
                    foreach (FileInfo f in fi)
                    {
                        if (f.Name.ToLower().Replace(f.Extension, "") == message.ToLower())
                        {
                            found = true;
                            string showLog = loadTextFile(Path.Combine(Server.userFilePath, @"logs" + Path.DirectorySeparatorChar + f.Name));
                            sendToUser(("{bold}{cyan}---[{red}Log: " + f.Name.Replace(f.Extension, "") + "{cyan}]").PadRight(103, '-') + "{reset}\r\n" + showLog + "\r\n{bold}{cyan}" + "".PadRight(80, '-') + "{reset}", true, false, false);
                        }
                    }
                    if (!found)
                        sendToUser("No such log \"" + message + "\"", true, false, false);
                }
            }
            else
            {
                sendToUser("No logs available to view", true, false, false);
            }

        }

        public void cmdDlog(string message)
        {
            // Routine to delete logs
            string path = Path.Combine(Server.userFilePath, (@"logs" + Path.DirectorySeparatorChar));

            if (message == "")
                sendToUser("Syntax: dlog <log file>", true, false, false);
            else if (message.ToLower() == "all")
            {
                if (Directory.Exists(path))
                {
                    DirectoryInfo di = new DirectoryInfo(path);
                    FileInfo[] fi = di.GetFiles();
                    foreach (FileInfo f in fi)
                    {
                        f.Delete();
                    }
                    sendToUser("All logs files deleted", true, false, false);
                }
            }
            else
            {
                if (Directory.Exists(path))
                {
                    DirectoryInfo di = new DirectoryInfo(path);
                    FileInfo[] fi = di.GetFiles();
                    bool found = false;
                    foreach (FileInfo f in fi)
                    {
                        if (f.Name.Replace(f.Extension, "").ToLower() == message.ToLower())
                        {
                            f.Delete();
                            found = true;
                            sendToUser("Log file \"" + f.Name.Replace(f.Extension, "") + "\" deleted", true, false, false);
                        }
                    }
                    if (!found)
                        sendToUser("Log file \"" + message + "\" not found!", true, false, false);
                }
            }
        }

        public void cmdAlog(string message)
        {
            // Routine to append logs to main and then delete
            string path = Path.Combine(Server.userFilePath, (@"logs" + Path.DirectorySeparatorChar));
            string append = Path.Combine(Server.userFilePath, (@"old" + Path.DirectorySeparatorChar));

            if (message == "")
                sendToUser("Syntax: alog <all/log file>", true, false, false);
            else if (message.ToLower() == "all")
            {
                if (Directory.Exists(path))
                {
                    DirectoryInfo di = new DirectoryInfo(path);
                    FileInfo[] fi = di.GetFiles();
                    foreach (FileInfo f in fi)
                    {
                        if (!Directory.Exists(append))
                            Directory.CreateDirectory(append);

                        StreamReader s = new StreamReader(path + f.Name);
                        StreamWriter w = new StreamWriter(append + "append.log", true);
                        string log = "";
                        while ((log = s.ReadLine()) != null)
                        {
                            w.WriteLine("[" + f.Name + "] " + log);
                            w.Flush();
                        }
                        s.Close();
                        s.Dispose();
                        w.Close();
                        w.Dispose();

                        f.Delete();
                    }
                    sendToUser("All logs files backed up", true, false, false);
                }
            }
            else
            {
                if (Directory.Exists(path))
                {
                    DirectoryInfo di = new DirectoryInfo(path);
                    FileInfo[] fi = di.GetFiles();
                    bool found = false;
                    foreach (FileInfo f in fi)
                    {
                        if (f.Name.Replace(f.Extension, "").ToLower() == message.ToLower())
                        {
                            if (!Directory.Exists(append))
                                Directory.CreateDirectory(append);

                            StreamReader s = new StreamReader(path + f.Name);
                            StreamWriter w = new StreamWriter(append + "append.log", true);
                            string log = "";
                            while ((log = s.ReadLine()) != null)
                            {
                                w.WriteLine("[" + f.Name + "] " + log);
                            }
                            s.Close();
                            w.Close();
                            f.Delete();
                            found = true;
                            sendToUser("Log file \"" + f.Name.Replace(f.Extension, "") + "\" backed up", true, false, false);
                        }
                    }
                    if (!found)
                        sendToUser("Log file \"" + message + "\" not found!", true, false, false);
                }
            }
        }

        #endregion

    }
}
