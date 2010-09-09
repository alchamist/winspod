﻿using System;
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
    public class Connection
    {
        public struct commands
        {
            public string cmdText;
            public string cmdCall;
            public int level;
            public string helpSection;
        }

        public struct createUser
        {
            public int createStatus;
            public string username;
            public string tPassword;
        }

        public struct message
        {
            public DateTime Date;
            public string From;
            public string To;
            public string Subject;
            public string Body;
            public bool Read;    // Has the message been read?
            public bool Warning; // Is this a warning?
            public bool Deleted; // Is this message deleted
        }

        public struct objects
        {
            public string       Name;
            public string       Creator;
            public string       Owner;
            public Player.Rank  Rank;
            public string       Contents;
            public string       Description;
            public int          Weight;
            public gender       Gender;

            public bool         Deleted;

            public bool         CanDropOnFloor;         // If dropped, can this item be left on the floor?

            public unique       Unique;
            public actions      Actions;

            public struct unique
            {
                public bool     ToPlayer;               // Can a player have only one?
                public bool     ToSystem;               // Can there be only one across the system?
            }

            public struct actions
            {
                public string   Drop;
                public string   Eat;
                public string   Drink;
                public string   Examine;
                public string   Get;
                public string   Give;
                public string   Pick;
                public string   Throw;
                public string   Play;
                public string   Push;
                public string   Pull;
                public string   Shake;
                public string   Wield;
                public string   Poke;
                public string   Use;
                public string   Take;
            }

        }

        public enum gender
        {
            Unknown,
            Male,
            Female
        }


        static object               BigLock = new object();
        Socket                      socket;
        public StreamReader         Reader;
        public StreamWriter         Writer;
        static ArrayList            connections = new ArrayList();
        private int                 myNum;
        private int                 myState = 0; // 0 = new connection, 1 = username supplied, 2 = new player, 3 = password supplied, 4 = active
        private Player              myPlayer;
        private string              connPoint;
        private ArrayList           cmds = new ArrayList();
        public string               lastSent;
        public createUser           newUser = new createUser();

        private byte[]              echoOff = new byte[] { 0xFF, 0xFB, 0x01 };
        private byte[]              echoOn = new byte[] { 0xFF, 0xFC, 0x01 };

        System.Timers.Timer         heartbeat = new System.Timers.Timer(); // Timer for heartbeat : idle out and hchime etc
        private int                 lastHChimeHour = -1;

        public List<Room>           roomList = new List<Room>();

        public List<string>         history = new List<string>();

        public List<ClubChannel>    clubChannels = new List<ClubChannel>();

        public List<message>        messages = new List<message>();

        public List<message>        mail = new List<message>();

        public List<objects>        playerObjects = new List<objects>();

        public message              editMail = new message();
        public string               editText = "";

        public string               pwChange = "";

        public List<DateTime>       idleHistory = new List<DateTime>(0);

        public List<IPAddress>      IPBanList = new List<IPAddress>();
        public List<string>         NameBanList = new List<string>();

        
        public Connection(Socket socket, int conNum)
        {
            myNum = conNum;
            //myPlayer = new Player(conNum);
            this.socket = socket;
            Reader = new StreamReader(new NetworkStream(socket, false));
            Writer = new StreamWriter(new NetworkStream(socket, true));

            heartbeat.Interval = 1000;
            heartbeat.Elapsed += new System.Timers.ElapsedEventHandler(heartbeat_Elapsed);
            heartbeat.Start();

            messages = loadMessages();

            roomList = loadRooms();

            new Thread(ClientLoop).Start();
        }

        void heartbeat_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (myPlayer != null)
            {
                if (myPlayer.HourlyChime && DateTime.Now.Minute == 0 && DateTime.Now.Hour != lastHChimeHour && !myPlayer.InEditor)
                {
                    lastHChimeHour = DateTime.Now.Hour;
                    sendToUser("{bold}{red}{bell} [[Ding Dong. It is now " + (DateTime.Now.AddHours(myPlayer.JetLag)).ToShortTimeString() + "]{reset}", true, true, false);
                    flushSocket();
                }
                if (myPlayer.PlayerRank < (int)Player.Rank.Admin && !myPlayer.SpecialPrivs.noidle)
                {
                    TimeSpan ts = (TimeSpan)(DateTime.Now - myPlayer.LastActive);
                    if (ts.Seconds == 0 && ts.Minutes >= 20)
                    {
                        if (ts.Minutes == 20)
                        {
                            sendToUser("{bold}{red}You are 20 minutes idle. 10 minutes until auto-boot{reset}");
                            sendToStaff("[Idle] " + myPlayer.UserName + " just received the 20 minute idle warning!", (int)Player.Rank.Staff, true);
                            flushSocket(true);
                        }
                        else if (ts.Minutes == 25)
                        {
                            sendToUser("{bold}{red}You are 25 minutes idle. 5 minutes until auto-boot{reset}");
                            sendToStaff("[Idle] " + myPlayer.UserName + " just received the 25 minute idle warning!", (int)Player.Rank.Staff, true);
                            flushSocket(true);
                        }
                        else if (ts.Minutes == 29)
                        {
                            sendToUser("{bold}{red}You are 29 minutes idle. 1 minute until auto-boot{reset}");
                            sendToStaff("[Idle] " + myPlayer.UserName + " just received the 29 minute idle warning!", (int)Player.Rank.Staff, true);
                            flushSocket(true);
                        }
                        else if (ts.Minutes == 30)
                        {
                            myPlayer.IdledCount++;
                            myPlayer.SavePlayer();
                            sendToUser("{bold}{red}You are 30 minutes idle. Goodbye!{reset}");
                            sendToStaff("[Idle] " + myPlayer.UserName + " has just been auto-booted for idling!", (int)Player.Rank.Staff, true);
                            flushSocket(true);
                            Disconnect();
                        }
                    }
                }
            }

            foreach (Room r in roomList)
            {
                string roomMessage = r.timerFire();
                if (roomMessage != "")
                {
                    sendToRoom("\r\n" + roomMessage, roomMessage, r.systemName, "");
                }

            }

            for (int i = connections.Count - 1; i >= 0; i--)
            {
                Connection c = (Connection)connections[i];
                if (!c.socket.Connected)
                    connections.RemoveAt(i);
            }

            if (Server.shutdownSecs > -1 && myPlayer != null)
            {
                if (Server.shutdownSecs == 3600)
                    sendToUser("^RWarning: ^N&t will shut down in one hour!", true, false, false);
                else if (Server.shutdownSecs == 1800)
                    sendToUser("^RWarning: ^N&t will shut down in 30 minutes!", true, false, false);
                else if (Server.shutdownSecs == 900)
                    sendToUser("^RWarning: ^N&t will shut down in 15 minutes!", true, false, false);
                else if (Server.shutdownSecs == 600)
                    sendToUser("^RWarning: ^N&t will shut down in 10 minutes!", true, false, false);
                else if (Server.shutdownSecs == 300)
                    sendToUser("^RWarning: ^N&t will shut down in 5 minutes!", true, false, false);
                else if (Server.shutdownSecs == 60)
                    sendToUser("^RWarning: ^N&t will shut down in 1 minute!", true, false, false);
                else if (Server.shutdownSecs == 30)
                    sendToUser("^RWarning: ^N&t will shut down in 30 seconds!", true, false, false);
                else if (Server.shutdownSecs == 10)
                    sendToUser("^RWarning: ^N&t will shut down in 10 ...!", true, false, false);
                else if (Server.shutdownSecs == 9)
                    sendToUser("^RWarning: ^N&t will shut down in 9 ...!", true, false, false);
                else if (Server.shutdownSecs == 8)
                    sendToUser("^RWarning: ^N&t will shut down in 8 ...!", true, false, false);
                else if (Server.shutdownSecs == 7)
                    sendToUser("^RWarning: ^N&t will shut down in 7 ...!", true, false, false);
                else if (Server.shutdownSecs == 6)
                    sendToUser("^RWarning: ^N&t will shut down in 6 ...!", true, false, false);
                else if (Server.shutdownSecs == 5)
                    sendToUser("^RWarning: ^N&t will shut down in 5 ...!", true, false, false);
                else if (Server.shutdownSecs == 4)
                    sendToUser("^RWarning: ^N&t will shut down in 4 ...!", true, false, false);
                else if (Server.shutdownSecs == 3)
                    sendToUser("^RWarning: ^N&t will shut down in 3 ...!", true, false, false);
                else if (Server.shutdownSecs == 2)
                    sendToUser("^RWarning: ^N&t will shut down in 2 ...!", true, false, false);
                else if (Server.shutdownSecs == 1)
                    sendToUser("^RWarning: ^N&t will shut down in 1 ...!", true, false, false);
                else if (Server.shutdownSecs == 0)
                {
                    sendToUser("^RWarning: ^N&t is shutting down now ...!", true, false, false);
                }

            }
        }

        #region Socket stuff

        void ClientLoop()
        {
            try
            {
                lock (BigLock)
                {
                    OnConnect();
                }
                while (true)
                {
                    lock (BigLock)
                    {
                        foreach (Connection conn in connections)
                        {
                            try
                            {
                                conn.Writer.Flush();
                            }
                            catch (Exception e)
                            {
                                logError(e.ToString(), "Socket");
                            }
                        }
                    }
                    string line = null;
                    if (socket.Connected && Reader.BaseStream.CanRead)
                    {
                        try
                        {
                            line = Reader.ReadLine();
                        }
                        catch//(Exception e)
                        {
                            //logError(e.ToString(), "socket");
                        }
                    }
                    else
                        OnDisconnect();
                    if (line == null)
                    {
                        break;
                    }
                    lock (BigLock)
                    {
                        ProcessLine(line);
                    }
                }
            }
            finally
            {
                lock (BigLock)
                {
                    socket.Close();
                    OnDisconnect();
                }
            }
        }

        void OnConnect()
        {
            connPoint = this.socket.RemoteEndPoint.ToString();
            connPoint = connPoint.Substring(0, connPoint.IndexOf(":"));

            IPAddress test = null;

            if (IPAddress.TryParse(connPoint, out test))
            {
                if (IpIsBanned(test))
                {
                    Writer.WriteLine("Sorry, this IP address has been banned from the system");
                    Writer.Flush();
                    socket.Close();
                    return;
                }
            }

            Version vrs = Assembly.GetExecutingAssembly().GetName().Version;
            string greeting = AnsiColour.Colorise(loadTextFile(@"files" + Path.DirectorySeparatorChar + "greeting.txt") + "{reset}");
            Writer.WriteLine(AnsiColour.Colorise("{bold}{white}\nWinspod II v" + vrs.Major + "." + vrs.Minor + " Build: " + vrs.Build + " Revision: " + vrs.Revision + "{reset}"));

            if (greeting!="")
                Writer.WriteLine(greeting);
            
            if (AppSettings.Default.LockLevel > 0 )
                Writer.WriteLine("\r\nSystem currently locked to: " +  rankName(AppSettings.Default.LockLevel) + " and above");

            Writer.Write(AnsiColour.Colorise("\r\n\r\n{bold}{white}Please enter your username: {reset}"));
            
            myState = 1;
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] New connection from " + connPoint);
            connections.Add(this);
            //sendToRoom(myPlayer.UserName + " " + myPlayer.EnterMsg, "", myPlayer.UserRoom, myPlayer.UserName);
        }

        void OnDisconnect()
        {
            doInform(false);

            if (myPlayer != null)
                logConnection(myPlayer.UserName, myPlayer.CurrentIP, DateTime.Now);

            myPlayer = null;
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Disconnect: " + connPoint);
            connections.Remove(this);

        }

        public void Disconnect()
        {
            myPlayer = null;
            socket.Shutdown(SocketShutdown.Both);
            connections.Remove(this);
        }

        #endregion

        #region ProcessCommand

        void ProcessLine(string line)
        {
            ProcessLine(line, false);
        }

        void ProcessLine(string line, bool noAlias)
        {
            line = cleanLine(line);
            if (myPlayer!=null)
                loadCommands();

            if (myState == 1)
            {
                if (NameIsBanned(line))
                {
                    Writer.Write(AnsiColour.Colorise("^HSorry, that name is not allowed on this system. Please try again: ^N"));
                    return;
                }
                //username supplied .. 
                //
                // Check to see if they have just done a "who"
                //
                if (line.Trim().ToLower() == "who" || line.Trim().ToLower() == "w")
                {
                    string output = offlineWho();
                    Writer.Write(AnsiColour.Colorise(output + "\r\n{bold}Please enter your username: {reset}"));
                }
                else if (line.Trim().ToLower() == "quit")
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
                else
                {
                    if (Regex.Replace(line, @"\W*", "") != line && newUser.createStatus==0)
                    {
                        Writer.Write(AnsiColour.Colorise("{bold}Sorry, only alphanumiric characters and no spaces\r\nPlease enter a valid username: {reset}", false));
                    }
                    else if (line.Trim().Length > 2)
                    {
                        if (newUser.createStatus == 0)
                        {
                            // Check to see if any players exist - if not then we need to make this person a god!
                            DirectoryInfo di = new DirectoryInfo(Path.Combine(Server.userFilePath,@"players" + Path.DirectorySeparatorChar));
                            DirectoryInfo[] dirs = null;
                            FileInfo[] files = null;
                            if (di.Exists)
                            {
                                dirs = di.GetDirectories();
                                files = di.GetFiles();
                            }
                            //if (dirs == null || dirs.Length == 0) // There are no player files!
                            if (files == null || files.Length == 0) // There are no player files!
                            {
                                newUser.createStatus = 1;
                                newUser.username = line.Trim();
                                myPlayer = Player.LoadPlayer(line.Trim(), myNum);
                                myPlayer.UserName = line.Trim();
                                sendEchoOff();
                                sendToUser("Please enter a password: ",false, false, false);
                                Writer.Flush();
                            }
                            else
                            {
                                myPlayer = Player.LoadPlayer(line.Trim(), myNum);

                                if ((AppSettings.Default.LockLevel > 0 && myPlayer.PlayerRank < AppSettings.Default.LockLevel))
                                {
                                    Writer.WriteLine("Sorry, the system is currently locked to " + (Player.Rank)AppSettings.Default.LockLevel + " only. Please try again later");
                                    Writer.Flush();
                                    Disconnect();
                                }
                                else
                                {
                                    if (myPlayer.NewPlayer)
                                    {
                                        //bool reconnect = false;
                                        //foreach (Connection conn in connections)
                                        //{
                                        //    if (conn.myPlayer != null && conn.myPlayer.UserName != null && conn.myPlayer.UserName.ToLower() == line.ToLower().Trim() && conn != this)
                                        //    {
                                        //        myPlayer = conn.myPlayer;
                                        //        conn.Disconnect();
                                        //        conn.socket.Close();
                                        //        reconnect = true;
                                        //    }
                                        //}

                                        //myState = 2;
                                        myState = 9;
                                        myPlayer.UserName = line.Trim();
                                        myPlayer.CurrentIP = connPoint;
                                        myPlayer.CurrentLogon = DateTime.Now;
                                        myPlayer.LastActive = DateTime.Now;
                                        //doPrompt();

                                        //if (!reconnect)
                                        //{
                                            myState = 2;
                                            string welcome = AnsiColour.Colorise(loadTextFile(@"files" + Path.DirectorySeparatorChar + "welcome.txt") + "{reset}");
                                            if (welcome != "")
                                            {
                                                sendToUser("{bold}{cyan}---[{red}Welcome{cyan}]".PadRight(103, '-') + "{reset}\r\n\r\n" + welcome + "\r\n{bold}{cyan}" + "".PadRight(80, '-') + "{reset}\r\nPress enter to continue");
                                            }
                                            foreach (Connection c in connections)
                                            {
                                                // Newbie notification ...
                                                if (c.socket.Connected && c.myPlayer != null && c.myPlayer.PlayerRank >= (int)Player.Rank.Guide && c.myPlayer.OnDuty)
                                                    sendToUser("{bold}{green}[Newbie alert]{reset} " + myPlayer.UserName + " has just connected" + (c.myPlayer.PlayerRank >= (int)Player.Rank.Admin ? " from ip " + myPlayer.CurrentIP : ""), c.myPlayer.UserName, true, c.myPlayer.DoColour, false, false);
                                            }
                                            //sendToUser("New player!!", true, true, false);
                                            //sendToRoom(myPlayer.ColourUserName + " " + myPlayer.LogonMsg, "");
                                        //}
                                        //else
                                        //{
                                        //    sendToRoom(myPlayer.ColourUserName + " " + "briefly phases out and back into existance");
                                        //}

                                        Writer.Flush();
                                    }

                                    else
                                    {
                                        //bool reconnect = false;
                                        foreach (Connection conn in connections)
                                        {
                                            if (conn.myPlayer != null && conn.myPlayer.UserName != null && conn.myPlayer.UserName.ToLower() == line.ToLower().Trim() && conn != this)
                                            {
                                                myPlayer = conn.myPlayer;
                                                //conn.Disconnect();
                                                //conn.socket.Close();
                                                //reconnect = true;
                                            }
                                        }
                                        //if (reconnect)
                                        //    sendToRoom(myPlayer.ColourUserName + " " + "briefly phases out and back into existance","");

                                        //sendEchoOff();
                                        //sendToUser("{bold}Please enter your password: {reset}", myPlayer.UserName, true, myPlayer.DoColour, false, false);

                                        socket.Send(echoOff);
                                        Writer.Write(AnsiColour.Colorise("{bold}Please enter your password: {reset}"));
                                        myState = 9;
                                    }
                                }
                            }
                        }
                        else if (newUser.createStatus == 1)
                        {
                            // Password entered - reenter to confirm
                            newUser.tPassword = line.Trim();
                            newUser.createStatus = 2;
                            sendToUser("\r\nPlease re-enter password to confirm: ", false, false, false);
                        }
                        else if (newUser.createStatus == 2)
                        {
                            if (line.Trim() == newUser.tPassword)
                            {
                                sendEchoOn();
                                sendToUser("\r\nPlease enter your e-mail address: ", false, false, false);
                                newUser.createStatus = 3;
                            }
                            else
                            {
                                sendToUser("\r\nPasswords do not match\r\nPlease enter a password: ", false, false, false);
                                newUser.createStatus = 1;
                            }
                        }
                        else if (newUser.createStatus == 3)
                        {
                            if (testEmailRegex(line.Trim()))
                            {
                                myPlayer.UserName = newUser.username;
                                myPlayer.Password = newUser.tPassword;
                                myPlayer.CurrentIP = connPoint;
                                myPlayer.CurrentLogon = DateTime.Now;
                                myPlayer.LastActive = DateTime.Now;
                                myPlayer.ResBy = "System";
                                myPlayer.PlayerRank = (int)Player.Rank.HCAdmin;
                                myPlayer.ResDate = DateTime.Now;
                                myPlayer.Title = "is da admin";
                                myPlayer.EmailAddress = line.Trim();
                                Player.privs p = new Player.privs();
                                p.builder = true;
                                p.tester = true;
                                p.noidle = true;
                                myPlayer.SpecialPrivs = p;
                                myPlayer.NewPlayer = false;

                                myState = 10;
                                sendToUser("\r\nWelcome, " + myPlayer.ColourUserName + ". You are now the admin of the system", true);
                                myPlayer.SavePlayer();
                                doPrompt();
                            }
                            else
                            {
                                sendToUser("Sorry, that is not a valid e-mail address.\r\n\r\nPlease enter your e-mail address: ", false, false, false);
                            }
                        }
                    }
                    else
                        Writer.Write(AnsiColour.Colorise("{bold}Please enter a valid username (minimum 3 characters): {reset}", false));
                }
            }
            else if (myState == 2)
            {
                // new player
                myState = 3;
                string rules = AnsiColour.Colorise(loadTextFile(@"files" + Path.DirectorySeparatorChar + "rules.txt") + "{reset}");
                if (rules != "")
                {
                    sendToUser("{bold}{cyan}---[{red}The Rules{cyan}]".PadRight(103, '-') + "{reset}\r\n\r\n" + rules + "\r\n{bold}{cyan}" + "".PadRight(80, '-') + "{reset}\r\nPress enter to continue");
                }
                
            }
            else if (myState == 3)
            {
                myState = 4;
                string disclaimer = AnsiColour.Colorise(loadTextFile(@"files" + Path.DirectorySeparatorChar + "disclaimer.txt") + "{reset}");
                if (disclaimer != "")
                {
                    sendToUser("{bold}{cyan}---[{red}Disclaimer{cyan}]".PadRight(103, '-') + "{reset}\r\n\r\n" + disclaimer + "\r\n");
                }
            }
            else if (myState == 4)
            {
                if (line.Trim().ToLower() == "agree")
                {
                    myState = 10;
                    //sendToUser("New player!!", true, true, false);
                    sendToRoom(myPlayer.ColourUserName + " " + myPlayer.LogonMsg, "");
                    cmdLook("");
                    doPrompt();
                }
                else if (line.Trim().ToLower() == "quit")
                {
                    Disconnect();
                }
                else
                {
                    sendToUser("Please enter \"agree\" or \"quit\"");
                }
            }
            else if (myState == 5)
            {
                // Just been granted residency - adding e-mail address
                string eMail = line.Trim();
                if (!testEmailRegex(eMail))
                {
                    sendToUser("Sorry, that is not recognised as a valid e-mail address.\r\nPlease enter your e-mail address", true, false, false);
                }
                else
                {
                    myState = 6;
                    sendEchoOff();
                    myPlayer.EmailAddress = eMail;
                    sendToUser("E-mail set to " + eMail + "\r\n\r\nPlease now enter a password. Passwords are case sensitive.\r\nPassword:");
                }
            }
            else if (myState == 6)
            {
                // Password entered - reenter to check for accuracy
                string pword = line.Trim();
                if (pword == "")
                    sendToUser("Blank passwords are not allowed\r\nPassword: ");
                else
                {
                    myPlayer.Password = pword;
                    myState = 7;
                    sendToUser("Please re-enter your password: ");
                }
            }
            else if (myState == 7)
            {
                string pword = line.Trim();
                if (!myPlayer.checkPassword(pword))
                {
                    // Passwords don't match ...
                    myState = 6;
                    sendToUser("Passwords do not match, try again\r\nPassword:");
                }
                else
                {
                    // Passwords match - we can proceed!
                    sendEchoOn();
                    myState = 10;
                    myPlayer.PlayerRank = (int)Player.Rank.Member;
                    sendToUser("Congratulations. You are now a resident of &t.", true, false, false);
                    sendToRoom(myPlayer.UserName + " has just been granted residency by " + myPlayer.ResBy);
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == myPlayer.ResBy.ToLower())
                        {
                            c.myPlayer.ResCount++;
                            c.myPlayer.SavePlayer();
                        }
                    }
                    myPlayer.SavePlayer();
                }
            }
            else if (myState == 9)
            {
                // password supplied
                if (line.Trim().ToLower() == "quit")
                {
                    sendEchoOn();
                    Writer.Write(AnsiColour.Colorise("\r\n{bold}Please enter your username: {reset}"));
                    myState = 1;
                }
                else if (myPlayer.checkPassword(line.Trim()) || myPlayer.NewPlayer)
                {
                    // Need to check if the system is locked to staff etc

                    if ((AppSettings.Default.LockLevel > 0 && myPlayer.PlayerRank < AppSettings.Default.LockLevel))
                    {
                        sendToUser("Sorry, the system is currently locked to " + (Player.Rank)AppSettings.Default.LockLevel + " only. Please try again later");
                        Disconnect();
                    }
                    else
                    {
                        sendEchoOn();
                        bool reconnect = false;
                        foreach (Connection conn in connections)
                        {
                            if (conn.myPlayer != null && conn.myPlayer.UserName != null && conn.myPlayer.UserName.ToLower() == myPlayer.UserName.ToLower() && conn != this)
                            {
                                myPlayer = conn.myPlayer;
                                conn.socket.Close();
                                //conn.Disconnect();
                                reconnect = true;
                            }
                        }

                        myState = 10;
                        Server.playerCount++;
                        if (!reconnect)
                        {
                            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Login: " + myPlayer.UserName);
                            showMOTD(false);
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                                cmdCheckLogs("");
                            checkMail();

                            sendToUser("\r\nLast login " + myPlayer.LastLogon.ToShortDateString() + " from " + myPlayer.LastIP, true, false, false);
                            doWarnings();

                            doLAlias();

                            sendToRoom(myPlayer.UserName + " " + myPlayer.EnterMsg, "", myPlayer.UserRoom, myPlayer.UserName);
                            myPlayer.LoginCount++;
                            myPlayer.SavePlayer();

                            doInform(true);
                        }
                        else
                        {
                            sendToRoom(myPlayer.ColourUserName + " " + "briefly phases out and back into existance", "\r\n", false, true);
                        }

                        myPlayer.CurrentIP = connPoint;
                        myPlayer.CurrentLogon = DateTime.Now;
                        myPlayer.LastActive = DateTime.Now;

                        doPrompt();
                    }
                }
                else
                {
                    string message = AnsiColour.Colorise("\r\n{bold}{red}Password incorrect{white}\r\nPlease enter your password: {reset}");
                    Writer.WriteLine(message);
                    //sendToUser("{bold}{red}Password incorrect{white}\r\nPlease enter your password: {reset}", myPlayer.UserName, true, myPlayer.DoColour, false, false);
                }
            }
            else if (myState == 10)
            {
                string cmd = line.Trim();
                bool adminIdle = false;

                if (myPlayer.InMailEditor)
                {
                    myPlayer.LastActive = DateTime.Now;
                    mailEdit(line);
                }
                else if (myPlayer.InDescriptionEditor)
                {
                    myPlayer.LastActive = DateTime.Now;
                    descriptionEdit(line);
                }
                else if (myPlayer.InRoomEditor)
                {
                    myPlayer.LastActive = DateTime.Now;
                    roomEdit(line);
                }
                else if (cmd != "")
                {
                    if (cmd.Substring(0, 1) == "#" && myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                    {
                        adminIdle = true;
                        cmd = cmd.Substring(1);
                    }

                    //if (cmd.ToLower() == "quit")
                    //{
                    //    Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Logout: " + myPlayer.UserName);
                    //    sendToRoom(myPlayer.UserName + " " + myPlayer.LogoffMsg, "");
                    //    myPlayer.TotalOnlineTime += Convert.ToInt16((DateTime.Now - myPlayer.CurrentLogon).TotalSeconds);
                    //    myPlayer.LastLogon = DateTime.Now;
                    //    myPlayer.LastIP = myPlayer.CurrentIP;
                    //    int longCheck = (int)(DateTime.Now - myPlayer.CurrentLogon).TotalSeconds;
                    //    if (longCheck > myPlayer.LongestLogin) myPlayer.LongestLogin = longCheck;
                    //    myPlayer.SavePlayer();
                    //    socket.Shutdown(SocketShutdown.Both);
                    //}
                    //else
                    //{
                    string shortCMD = cmd.Substring(0, 1);
                    string firstWord = "";
                    string message = "";

                    if (cmd.IndexOf(" ") != -1)
                    {
                        firstWord = cmd.Substring(0, cmd.IndexOf(" ")).Trim().ToLower();
                        message = cmd.Substring(cmd.IndexOf(" ")).Trim();
                    }
                    else
                    {
                        //firstWord = message;
                        firstWord = cmd;
                    }


                    bool found = false;
                    foreach (commands c in cmds)
                    {
                        //if (shortCMD == c.cmdText || firstWord == c.cmdText || cmd == c.cmdText)
                        if (firstWord == c.cmdText || cmd == c.cmdText)
                        {
                            try
                            {
                                if (myPlayer.Away && !adminIdle)
                                {
                                    myPlayer.Away = false;
                                    sendToUser("You set yourself as back", true, false, false);
                                }
                                MethodInfo mInfo = typeof(Connection).GetMethod(c.cmdCall);
                                mInfo.Invoke(this, new object[] { cmd.Substring(c.cmdText.Length).Trim() });
                            }
                            catch (Exception e)
                            {
                                logError("Error parsing command " + c.cmdText + ": " + e.ToString(), "Command");
                                sendToUser("Sorry, there has been an error", true, false, false);
                            }
                            finally
                            {
                                found = true;
                                Server.cmdUse(c.cmdText);
                                if (!adminIdle)
                                {
                                    myPlayer.LastActive = DateTime.Now;
                                    idleHistory.Add(DateTime.Now);
                                }
                                if (!noAlias && myState == 10)
                                    doPrompt();
                            }
                        }
                    }
                    if (!found && new Regex("[^a-zA-Z0-9\n\r\t ]", RegexOptions.IgnoreCase).IsMatch(shortCMD))
                    {
                        foreach (commands c in cmds)
                        {
                            if (shortCMD == c.cmdText)
                            {
                                try
                                {
                                    if (myPlayer.Away && !adminIdle)
                                    {
                                        myPlayer.Away = false;
                                        sendToUser("You set yourself as back", true, false, false);
                                    }
                                    MethodInfo mInfo = typeof(Connection).GetMethod(c.cmdCall);
                                    mInfo.Invoke(this, new object[] { cmd.Substring(c.cmdText.Length).Trim() });
                                }
                                catch (Exception e)
                                {
                                    logError("Error parsing command " + c.cmdText + ": " + e.ToString(), "Command");
                                    sendToUser("Sorry, there has been an error", true, false, false);
                                }
                                finally
                                {
                                    found = true;
                                    Server.cmdUse(c.cmdText);
                                    if (!adminIdle)
                                    {
                                        myPlayer.LastActive = DateTime.Now;
                                        idleHistory.Add(DateTime.Now);
                                    }
                                    if (!noAlias && myState == 10)
                                        doPrompt();
                                }
                            }
                        }
                    }
                    if (!found && !noAlias)
                        found = doAlias(cmd);

                    if (!found)
                        sendToUser("Huh?", true, true, false);

                    //if (!noAlias && found)
                    //    doPrompt();
                    //}
                }
                else
                {
                    doPrompt(myPlayer.UserName);
                }
            }
            else if (myState == 11)
            {
                // user is changing password, and has entered their existing password. Need to check it's correct and ask them to enter a new one
                if (myPlayer.checkPassword(line))
                {
                    myState = 12;
                    sendToUser("\r\nPlease enter your new password: ", false, false, false);
                }
                else
                {
                    myState = 10;
                    sendEchoOn();
                    sendToUser("\r\nIncorrect password. Update aborted", true, false, false);
                    doPrompt();
                }
            }
            else if (myState == 12)
            {
                // user is changing password, and has entered the first one. Need to check it's valid, store it and ask them to confirm
                string pword = line.Trim();
                if (pword == "")
                {
                    sendEchoOn();
                    sendToUser("\r\nBlank passwords not allowed. Aborting", true, false, false);
                    myState = 10;
                    doPrompt();
                }
                else
                {
                    pwChange = pword;
                    sendToUser("\r\nPlease re-enter your new password: ", false, false, false);
                    myState = 13;
                }
            }
            else if (myState == 13)
            {
                // user is changing password, and has re-entered the password. Need to check they match
                if (line.Trim() == pwChange)
                {
                    // Passwords match - update user
                    myPlayer.Password = pwChange;
                    myPlayer.SavePlayer();
                    sendToUser("\r\nPassword successfully updated", true, false, false);
                }
                else
                {
                    // Passwords do not match - abort update
                    sendToUser("\r\nPasswords do not match. Update aborted", true, false, false);
                }
                sendEchoOn();
                pwChange = "";
                myState = 10;
                doPrompt();
            }
        } 

        #endregion

        #region sendCommands

        #region sendToAll

        private void sendToAll(string msg)
        {
            sendToAll(msg, true);
        }

        private void sendToAll(string msg, bool newline)
        {
            foreach (Connection conn in connections)
            {
                if (conn.socket.Connected && conn.myPlayer != null)
                {
                    try
                    {
                        if (conn.myPlayer.CanHear(myPlayer.UserName))
                            conn.Writer.Write(AnsiColour.Colorise(msg, !conn.myPlayer.DoColour));
                    }
                    catch (Exception ex)
                    {
                        logError(ex.ToString(), "Socket write");
                    }
                }
            }
        }

        #endregion

        #region sendToUser

        private void sendToUser(string msg)
        {
            sendToUser(msg, myPlayer.UserName, true, false, false, true);
        }

        private void sendToUser(string msg, bool newline)
        {
            sendToUser(msg, myPlayer.UserName, newline, false, false, true);
        }

        private void sendToUser(string msg, bool newline, bool doPrompt)
        {
            sendToUser(msg, myPlayer.UserName, newline, false, doPrompt, true);
        }

        private void sendToUser(string msg, bool newline, bool doPrompt, bool doHistory)
        {
            sendToUser(msg, myPlayer.UserName, newline, false, doPrompt, doHistory);
        }

        private void sendToUser(string msg, string user)
        {
            sendToUser(msg, user, true, false, false, true);
        }

        private void sendToUser(string msg, string user, bool newline)
        {
            sendToUser(msg, user, newline, false, false, true);
        }

        private void sendToUser(string msg, string user, bool newline, bool removeColour, bool sendPrompt, bool doHistory)
        {
            foreach (Connection conn in connections)
            {
                if (conn.myPlayer != null && conn.myPlayer.UserName.ToLower() == user.ToLower() && msg != null && conn.myPlayer.CanHear(myPlayer.UserName))
                {
                    try
                    {
                        if (conn.socket.Connected)
                        {
                            string prefix = "";
                            if (conn.myPlayer != null && conn.lastSent == conn.myPlayer.Prompt && !msg.StartsWith(conn.myPlayer.Prompt) && conn.myPlayer.UserName != myPlayer.UserName)
                                prefix = "\r\n";
                            if (newline)
                                conn.Writer.WriteLine(prefix + AnsiColour.Colorise(msg, (removeColour || !conn.myPlayer.DoColour)));
                            else
                                conn.Writer.Write(prefix + AnsiColour.Colorise(msg, (removeColour || !conn.myPlayer.DoColour)));

                            conn.Writer.Flush();

                            conn.lastSent = msg;

                            if (doHistory)
                            {
                                conn.history.Add(msg);
                                if (conn.history.Count > 50)
                                    conn.history.RemoveAt(0);
                            }

                            if (sendPrompt)
                                doPrompt(user);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        logError(ex.ToString(), "Socket write");
                    }
                }
            }
        }

        #endregion

        #region sendToRoom

        private void sendToRoom(string msg)
        {
            sendToRoom(msg, msg, myPlayer.UserRoom, myPlayer.UserName, true, true, true);
        }

        private void sendToRoom(string msgToOthers, string msgToSender)
        {
            sendToRoom(msgToOthers, msgToSender, myPlayer.UserRoom, myPlayer.UserName, true, true, true);
        }

        private void sendToRoom(string msgToOthers, string msgToSender, string room, string sender)
        {
            sendToRoom(msgToOthers, msgToSender, room, sender, true, true, true);
        }

        private void sendToRoom(string msgToOthers, string msgToSender, bool senderPrompt, bool receiverPrompt)
        {
            sendToRoom(msgToOthers, msgToSender, myPlayer.UserRoom, myPlayer.UserName, true, senderPrompt, receiverPrompt);
        }

        private void sendToRoom(string msgToOthers, string msgToSender, string room, string sender, bool newline, bool senderPrompt, bool receiverPrompt)
        {
            foreach (Connection conn in connections)
            {
                if (conn.myPlayer != null && conn.myPlayer.UserName != sender && conn.myPlayer.UserRoom == room && !conn.myPlayer.InEditor && conn.myPlayer.CanHear(sender))
                {
                    //sendToUser(msgToOthers, conn.myPlayer.UserName, newline, conn.myPlayer.DoColour, receiverPrompt, true);
                    conn.sendToUser(msgToOthers, newline, receiverPrompt, true);
                }
            }
            if (msgToSender != "" && myPlayer != null)
            {
                //sendToUser(msgToSender, sender, newline, myPlayer.DoColour, senderPrompt, true);
                sendToUser(msgToSender, newline, senderPrompt, true);
            }
        }

        #endregion

        #region sendToStaff

        private void sendToStaff(string message, int rank, bool newline)
        {
            foreach (Connection conn in connections)
            {
                if (conn.socket.Connected && conn.myPlayer != null && conn.myPlayer.PlayerRank >= rank && myPlayer.onStaffChannel((Player.Rank)rank) && !conn.myPlayer.InEditor && conn.myPlayer.CanHear(myPlayer.UserName))
                {
                    string col = null;
                    switch (rank)
                    {
                        case (int)Player.Rank.Guide:
                            col = AppSettings.Default.GuideColour;
                            break;
                        case (int)Player.Rank.Staff:
                            col = AppSettings.Default.StaffColour;
                            break;
                        case (int)Player.Rank.Admin:
                            col = AppSettings.Default.AdminColour;
                            break;
                        case (int)Player.Rank.HCAdmin:
                            col = AppSettings.Default.HCAdminColour;
                            break;
                    }
                    sendToUser(col + message + "{reset}", conn.myPlayer.UserName, newline);
                }
            }
        }

        #endregion

        #region sendToChannel

        private void sendToChannel(string channel, string message, bool nohistory)
        {
            ClubChannel chan = ClubChannel.LoadChannel(channel);
            if (chan == null)
                sendToUser("Error sending to channel", true, false, false);
            else
            {
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null && chan.OnChannel(c.myPlayer.UserName) && !c.myPlayer.ClubChannelMute && !c.myPlayer.InEditor)
                    {
                        sendToUser(chan.FormatMessage(message), true, c.myPlayer.UserName != myPlayer.UserName, nohistory);
                    }
                }
            }
        }

        #endregion

        #endregion

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
            string path = Path.Combine(Server.userFilePath,@"connections" + Path.DirectorySeparatorChar + "connections.log");
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

            string path = Path.Combine(Server.userFilePath,(@"connections" + Path.DirectorySeparatorChar));
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
                            fields = new string[]{",",temp[2],temp[3],temp[4]};
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
            string path = Path.Combine(Server.userFilePath,(@"rooms" + Path.DirectorySeparatorChar));
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
                string path = Path.Combine(Server.userFilePath,@"logs" + Path.DirectorySeparatorChar);
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
            string path = Path.Combine(Server.userFilePath,(@"logs" + Path.DirectorySeparatorChar));
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
            string path = Path.Combine(Server.userFilePath,(@"logs" + Path.DirectorySeparatorChar));
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
            string path = Path.Combine(Server.userFilePath,(@"logs" + Path.DirectorySeparatorChar));

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
                            string showLog = loadTextFile(Path.Combine(Server.userFilePath,@"logs" + Path.DirectorySeparatorChar + f.Name));
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
            string path = Path.Combine(Server.userFilePath,(@"logs" + Path.DirectorySeparatorChar));

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
            string path = Path.Combine(Server.userFilePath,(@"logs" + Path.DirectorySeparatorChar));
            string append = Path.Combine(Server.userFilePath,(@"old" + Path.DirectorySeparatorChar));

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

        #region Methods

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
                                    sendToUser("You " + tellWord(text) + c.myPlayer.UserName + " \"" + wibbleText(text, false) + "{reset}\"", true, false);
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
                sendToRoom(myPlayer.UserName + wibbleText(message, true), "You emote: " + myPlayer.UserName + wibbleText(message, true), false, true);
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
                            sendToUser("{bold}{yellow}[" + myPlayer.UserName + "]{reset} " + wibbleText(message, false), c.myPlayer.UserName, true, c.myPlayer.DoColour, c.myPlayer.UserName == myPlayer.UserName ? false : true, true);
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
                    if (myPlayer.CanShout)
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

        public void cmdTellFriends(string message)
        {
            if (message == "")
                sendToUser("Syntax: tf <message>", true, false, false);
            else
            {
                int count = 0;
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null && myPlayer.isFriend(c.myPlayer.UserName))
                    {
                        count++;
                        if (!c.myPlayer.InEditor)
                            c.sendToUser("\r\n{bold}{green}(To friends) " + myPlayer.UserName + " " + sayWord(message, false) + " \"" + message + "\"", true, true, true);
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
            else
            {
                int count = 0;
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null && myPlayer.isFriend(c.myPlayer.UserName))
                    {
                        count++;
                        if (!c.myPlayer.InEditor)
                            c.sendToUser("\r\n{bold}{green}(To friends) " + myPlayer.UserName + (message.Substring(0,1) == "'" ? "" : " ") + message , true, true, true);
                    }
                }
                if (count == 0)
                    sendToUser("None of your friends are online right now");
                else
                    sendToUser("You emote to your friends: " + myPlayer.UserName + (message.Substring(0,1) == "'" ? "" : " ") + message, true, false, true);
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
                                        c.sendToUser("\r\n{bold}{green}(To " + (c.myPlayer.UserName == temp.UserName ? "your" : temp.UserName + "'s") + " friends) " + myPlayer.UserName + " " + sayWord(split[1], false) + " \"" + split[1] + "\"", true, true, true);
                                    }
                                }
                            }
                        }
                        if (count == 0)
                            sendToUser("None of " + temp.UserName + "'s friends can receive messages at the moment", true, false, false);
                        else
                            sendToUser("You " + sayWord(split[1], true) + " to " + temp.UserName + "'s friends \"" + split[1] + "\"", true, false, true);
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
                                        c.sendToUser("\r\n{bold}{green}(To " + (c.myPlayer.UserName == temp.UserName ? "your" : temp.UserName + "'s") + " friends) " + myPlayer.UserName + (split[1].Substring(0, 1) == "'" ? "" : " ") + split[1], true, true, true);
                                    }
                                }
                            }
                        }
                        if (count == 0)
                            sendToUser("None of " + temp.UserName + "'s friends can receive messages at the moment", true, false, false);
                        else
                            sendToUser("You emote to " + temp.UserName + "'s friends: " + myPlayer.UserName + (split[1].Substring(0,1)=="'" ? "" : " ") + split[1], true, false, true);
                    }
                }
            }
        }

        #endregion

        public void cmdListCommands(string message)
        {
            message = message.ToLower();
            if (message == "")
            {
                List<string> cmdCat = new List<string>();
                foreach (commands c in cmds)
                {
                    if (cmdCat.IndexOf(c.helpSection) == -1 && c.helpSection != "dnl")
                    {
                        cmdCat.Add(c.helpSection);
                    }
                }
                cmdCat.Sort();

                string fmtMsg = "Format: cmd [all|";
                foreach (string c in cmdCat)
                    fmtMsg += c + "|";
                fmtMsg = fmtMsg.Remove(fmtMsg.Length - 1) + "]";
                sendToUser(fmtMsg, true, false, false);
            }
            else
            {
                List<string> cmdCat = new List<string>();
                foreach (commands c in cmds)
                {
                    if ((c.helpSection == message || message == "all") && c.helpSection != "dnl")
                    {
                        cmdCat.Add(c.cmdText);
                    }
                }
                cmdCat.Sort();

                if (cmdCat.Count > 0)
                {
                    string line = "".PadLeft(80, '-');
                    string output = " Command listing for \"" + message + "\" ";
                    int middle = 39 - (int)((output.Length) / 2);
                    output = output.PadLeft(39+(middle/2), '-').PadRight(80, '-') + "\r\n";
                    foreach (string c in cmdCat)
                        output += c + ", ";
                    output = output.Remove(output.Length - 2) + ".\r\n" + line;
                    sendToUser(output, true, false, false);
                }
                else
                {
                    sendToUser("No commands available for " + message, true, false, false);
                }
            }

            
            
        }

        public void cmdWho(string message)
        {
            List<string> online = new List<string>();
            foreach (Connection c in connections)
            {
                if (c.myPlayer != null && !c.myPlayer.Invisible)
                    online.Add(c.myPlayer.ColourUserName);
            }
            if (online.Count == 0)
                sendToUser("There is nobody online", true, false, false);
            else if (online.Count == 1)
                sendToUser("There is one person online:\r\n" + online[0].ToString(), true, false, false);
            else
            {
                string output = "There are " + online.Count.ToString() + " people online:\r\n";
                foreach (string name in online)
                    output += name + ", ";
                sendToUser(output.Remove(output.Length - 2), true, false, false);
            }
        }

        public void cmdExamine(string message)
        {
            string[] target;
            bool online = false;
            Player ex = null;

            if (message == "" || message == "me")
            {
                target = new string[] {myPlayer.UserName};
            }
            else
                target = matchPartial(message);

            if (target.Length == 0)
                sendToUser("No such user \"" + message + "\"");
            else if (target.Length > 1)
                sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
            else
            {
                string ip = "";
                foreach (Connection c in connections)
                {
                    if (c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                    {
                        online = true;
                        ex = c.myPlayer;
                        ip = c.connPoint;
                    }
                }
                if (ex == null && !online)
                {
                    // User is offline

                    //string path = @"player" + Path.DirectorySeparatorChar + target[0].Substring(0, 1).ToUpper() + Path.DirectorySeparatorChar + target[0].ToLower() + ".xml";
                    //if (File.Exists(path))
                    //{
                    //    ex = Player.LoadPlayer(target[0], 0);
                    //}
                    ex = Player.LoadPlayer(target[0], 0);
                }
                if (ex != null)
                {
                    string line = "".PadRight(80, '-');
                    string output = ("{bold}{cyan}---[" + ex.UserName + "{bold}{cyan}]").PadRight(104,'-').Replace(ex.UserName, ex.ColourUserName) + "{reset}\r\n";
                    //output = output.PadRight(104, '-') + "{reset}\r\n";
                    output += (ex.Prefix + " " + ex.ColourUserName + " " + ex.Title).Trim() + "\r\n";
                    output += "{bold}{cyan}" + line + "{reset}\r\n";
                    if (ex.Tagline != "")
                    {
                        output += ex.Tagline + "\r\n{bold}{cyan}" + line + "{reset}\r\n";
                    }

                    if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                    {
                        if (!online || ex.Invisible)
                            output += "{bold}{blue}Last address {reset}".PadRight(48, ' ') + ": {blue}" + ex.LastIP + "{reset}\r\n";
                        else
                            output += "{bold}{blue}Current address {reset}".PadRight(48, ' ') + ": {blue}" + ex.CurrentIP + "{reset}\r\n";
                    }
                    if (online && !ex.Invisible)
                    {
                        output += "{bold}{blue}Online since {reset}".PadRight(48, ' ') + ": {blue}" + ex.CurrentLogon.ToString() + "{reset}\r\n";
                    }
                    else
                    {
                        output += "{bold}{blue}Last seen {reset}".PadRight(48, ' ') + ": {blue}" + ex.LastLogon.ToString() + "{reset}\r\n";
                    }
                    if (online && !ex.Invisible)
                    {
                        string time = (DateTime.Now - ex.CurrentLogon).ToString();
                        output += "{bold}{blue}Time Online {reset}".PadRight(48, ' ') + ": {blue}" + time.Remove(time.IndexOf('.')) + "{reset}\r\n";
                    }
                    string longest;
                    if ((DateTime.Now - ex.CurrentLogon).TotalSeconds > ex.LongestLogin && !ex.Invisible)
                        longest = (DateTime.Now - ex.CurrentLogon).ToString();
                    else
                        longest = (DateTime.Now.AddSeconds((double)ex.LongestLogin) - DateTime.Now).ToString();

                    output += "{bold}{blue}Longest Login {reset}".PadRight(48, ' ') + ": {blue}" + (longest.IndexOf('.') > -1 ? longest.Remove(longest.IndexOf('.')) : longest) + "{reset}\r\n";
                    output += "{bold}{blue}Previous Logins {reset}".PadRight(48, ' ') + ": {blue}" + ex.LoginCount.ToString() + "{reset}\r\n";
                    output += "{bold}{blue}Average Logon Time {reset}".PadRight(48, ' ') + ": {blue}" + ex.AverageLoginTime.ToString() + "{reset}\r\n";

                    //string tOnline = TimeSpan.FromSeconds((DateTime.Now - ex.CurrentLogon).TotalSeconds + ex.TotalOnlineTime).Days.ToString();
                    //output += "{bold}{blue}Total Online Time {reset}".PadRight(48, ' ') + ": {blue}" + tOnline.Remove(tOnline.IndexOf('.')) + "{reset}\r\n";
                    string tOnline = formatTimeNoZeros(TimeSpan.FromSeconds((DateTime.Now - ex.CurrentLogon).TotalSeconds + ex.TotalOnlineTime));
                    output += "{bold}{blue}Total Online Time {reset}".PadRight(48, ' ') + ": {blue}" + tOnline + "{reset}\r\n";

                    if (ex.PlayerRank > (int)Player.Rank.Newbie)
                    {
                        output += "{bold}{blue}Resident Since {reset}".PadRight(48, ' ') + ": {blue}" + ex.ResDate.ToString() + "{reset}\r\n";
                        TimeSpan rAge = TimeSpan.FromSeconds((DateTime.Now - ex.ResDate).TotalSeconds);
                        int rAgeYears = (int)Math.Floor(rAge.TotalDays/365);
                        int rAgeDays = (int)rAge.TotalDays%365;
                        output += "{bold}{blue}Resident age {reset}".PadRight(48, ' ') + ": {blue}" + (rAgeYears > 0 ? (rAgeYears.ToString() + " Year, " + (rAgeYears > 1 ? "s" : "")) : "") + rAgeDays.ToString() + " day" + (rAgeDays == 1 ? "" : "s") + " {reset}\r\n";
                        output += "{bold}{blue}Ressed by {reset}".PadRight(48, ' ') + ": {blue}" + ex.ResBy + "{reset}\r\n";
                    }

                    if (ex.EmailAddress != "" && ((ex.isFriend(myPlayer.UserName) && ex.EmailPermissions == (int)Player.ShowTo.Friends) || ex.EmailPermissions == (int)Player.ShowTo.Public || myPlayer.PlayerRank >= (int)Player.Rank.Admin || myPlayer.UserName == ex.UserName))
                        output += "{bold}{magenta}E-mail Address {reset}".PadRight(51, ' ') + ": {magenta}" + ex.EmailAddress + "{reset}\r\n";
                    if (ex.JabberAddress != "")
                        output += "{bold}{magenta}Jabber {reset}".PadRight(51, ' ') + ": {magenta}" + ex.JabberAddress + "{reset}\r\n";
                    if (ex.ICQAddress != "")
                        output += "{bold}{magenta}ICQ {reset}".PadRight(51, ' ') + ": {magenta}" + ex.ICQAddress + "{reset}\r\n";
                    if (ex.MSNAddress != "")
                        output += "{bold}{magenta}MSN {reset}".PadRight(51, ' ') + ": {magenta}" + ex.MSNAddress + "{reset}\r\n";
                    if (ex.YahooAddress != "")
                        output += "{bold}{magenta}Yahoo {reset}".PadRight(51, ' ') + ": {magenta}" + ex.YahooAddress + "{reset}\r\n";
                    if (ex.SkypeAddress != "")
                        output += "{bold}{magenta}Skype {reset}".PadRight(51, ' ') + ": {magenta}" + ex.SkypeAddress + "{reset}\r\n";
                    if (ex.HomeURL != "")
                        output += "{bold}{magenta}Home URL {reset}".PadRight(51, ' ') + ": {magenta}" + ex.HomeURL + "{reset}\r\n";
                    if (ex.WorkURL != "")
                        output += "{bold}{magenta}Work URL {reset}".PadRight(51, ' ') + ": {magenta}" + ex.WorkURL + "{reset}\r\n";

                    if (ex.RealName != "")
                        output += "{bold}{cyan}IRL Name {reset}".PadRight(48, ' ') + ": {cyan}" + ex.RealName + "{reset}\r\n";
                    if (ex.DateOfBirth != DateTime.MinValue)
                        output += "{bold}{cyan}Age {reset}".PadRight(48, ' ') + ": {cyan}" + ((int)((DateTime.Now.Subtract(ex.DateOfBirth)).Days / 365.25)).ToString() + "{reset}\r\n";
                    if (ex.Occupation != "")
                        output += "{bold}{cyan}Occupation {reset}".PadRight(48, ' ') + ": {cyan}" + ex.Occupation + "{reset}\r\n";
                    if (ex.Hometown != "")
                        output += "{bold}{cyan}Home Town {reset}".PadRight(48, ' ') + ": {cyan}" + ex.Hometown + "{reset}\r\n";
                    
                    output += "{bold}{cyan}Local Time {reset}".PadRight(48, ' ') + ": {cyan}" + DateTime.Now.AddHours(ex.JetLag).ToShortTimeString() + "{reset}\r\n";

                    output += "{bold}{yellow}Gender {reset}".PadRight(50, ' ') + ": {yellow}" + (gender)ex.Gender + "{reset}\r\n";
                    output += "{bold}{yellow}Rank {reset}".PadRight(50, ' ') + ": " + rankName(ex.PlayerRank) + "{reset}\r\n";
                    output += "{bold}{yellow}Blocking Shouts {reset}".PadRight(50, ' ') + ": {yellow}" + (ex.HearShouts ? "No" : "Yes") + "{reset}\r\n";

                    if (ex.PlayerRank >= (int)Player.Rank.Staff)
                        output += "{bold}{yellow}Has ressed{reset}".PadRight(50, ' ') + ": {yellow}" + ex.ResCount.ToString() + " player" + (ex.ResCount == 1 ? "" : "s") + "{reset}\r\n";

                    Player.privs pPrivs = ex.SpecialPrivs;
                    if (pPrivs.builder || pPrivs.tester)
                    {
                        output += "{bold}{yellow}Special Privs {reset}".PadRight(50, ' ') + ": {yellow}";
                        if (pPrivs.builder) output += "builder,";
                        if (pPrivs.tester) output += "tester,";
                        if (pPrivs.noidle) output += "noidle ";
                        output = output.Remove(output.Length - 1, 1) + "{reset}\r\n";
                    }

                    output += "{bold}{yellow}On Channels {reset}".PadRight(50, ' ') + ": {yellow}" + getChannels(ex.UserName) + "{reset}\r\n";
                    if (ex.InformTag != "")
                        output += "{bold}{yellow}Inform Tag {reset}".PadRight(50, ' ') + ": {yellow}[" + ex.InformTag + "{reset}{yellow}]{reset}\r\n";

                    if (myPlayer.PlayerRank >= (int)Player.Rank.Staff)
                    {
                        output += "{bold}{red}Kicked {reset}".PadRight(47, ' ') + ": {red}" + ex.KickedCount.ToString() + "{reset}\r\n";
                        output += "{bold}{red}Warned {reset}".PadRight(47, ' ') + ": {red}" + ex.WarnedCount.ToString() + "{reset}\r\n";
                        output += "{bold}{red}Idled {reset}".PadRight(47, ' ') + ": {red}" + ex.IdledCount.ToString() + "{reset}\r\n";
                        output += "{bold}{red}Slapped {reset}".PadRight(47, ' ') + ": {red}" + ex.SlappedCount.ToString() + "{reset}\r\n";
                        output += "{bold}{red}Tags {reset}".PadRight(47, ' ') + ": {red}" + (ex.Git || ex.AutoGit ? (ex.Git ? "[GIT] " : "") + (ex.AutoGit ? "[AUTOGIT]" : "") : "None") + "{reset}\r\n";
                        if (ex.Wibbled)
                            output += "{bold}{red}Wibbled By {reset}".PadRight(47, ' ') + ": {red}" + ex.WibbledBy + "{reset}\r\n";
                        
                    }
                    output += "{bold}{cyan}" + line + "{reset}";

                    sendToUser(output, true, false, false);

                }
                else
                {
                    logError("Error getting offline examine details for " + target[0], "File I/O");
                    sendToUser("Sorry, there has been an error", true, false, false);
                }
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

        public void cmdNoShout(string message)
        {
            if (myPlayer.HearShouts)
                sendToUser("You are now blocking shouts", true, false, false);
            else
                sendToUser("You are now hearing shouts again", true, false, false);
            myPlayer.HearShouts = !myPlayer.HearShouts;
            myPlayer.SavePlayer();
        }

        public void cmdFullWho(string message)
        {
            string line = "{bold}{cyan}".PadRight(92, '-') + "{reset}";
            string output = "{bold}{cyan}---[{green}Who{cyan}]".PadRight(105, '-') + "{reset}\r\n{##UserCount}\r\n";
            output += "{bold}{cyan}--[{red}Time{cyan}]---[{red}R{cyan}]".PadRight(114 ,'-') + "{reset}\r\n";
            int userCount = 0;
            foreach (Connection conn in connections)
            {
                if (conn.myPlayer != null)
                {
                    if (!conn.myPlayer.Invisible || conn.myPlayer.PlayerRank < (int)Player.Rank.Admin)
                    {
                        output += "{magenta}[";
                        if (conn.myPlayer.Away)
                            output += "{red}--Away--{magenta}]{reset}";
                        else
                        {
                            string oTime = (DateTime.Now - conn.myPlayer.CurrentLogon).ToString();
                            output += oTime.Substring(0, oTime.IndexOf('.')).Trim() + "]{reset}";
                        }

                        output += " " + conn.myPlayer.GetRankColour() + "[" + ((Player.Rank)conn.myPlayer.PlayerRank).ToString().Substring(0, 1) + "]{reset} ";
                        output += (conn.myPlayer.Prefix + " " + conn.myPlayer.ColourUserName + " " + conn.myPlayer.Title).Trim() + "\r\n";
                        userCount++;
                    }
                }
            }
            output += line + "\r\n{tname} has been up for " + formatTime(DateTime.Now - Server.startTime) + "\r\n" + line;

            if (userCount == 1)
                output = output.Replace("{##UserCount}", "There is one person here");
            else
                output = output.Replace("{##UserCount}", "There are " + userCount.ToString() + " people here");

            sendToUser(output, true, false, false);
        }

        public void cmdIdle(string message)
        {
            string line = "{bold}{cyan}".PadRight(92, '-') + "{reset}";
            string output = "{bold}{cyan}---[{green}Idle{cyan}]".PadRight(104, '-') + "{reset}\r\n{##UserCount}\r\n";
            output += "{bold}{cyan}--[{red}Time{cyan}]---[{red}R{cyan}]".PadRight(114, '-') + "{reset}\r\n";
            int userCount = 0;
            foreach (Connection conn in connections)
            {
                if (conn.myPlayer != null)
                {
                    if (!conn.myPlayer.Invisible || conn.myPlayer.PlayerRank < (int)Player.Rank.Admin)
                    {
                        output += "{magenta}[";
                        if (conn.myPlayer.Away)
                            output += "{red}--Away--{magenta}]{reset}";
                        else
                        {
                            string oTime = (DateTime.Now - conn.myPlayer.LastActive).ToString();
                            output += oTime.Substring(0, oTime.IndexOf('.')).Trim() + "]{reset}";
                        }

                        output += " " + conn.myPlayer.GetRankColour() + "[" + ((Player.Rank)conn.myPlayer.PlayerRank).ToString().Substring(0, 1) + "]{reset} ";
                        output += (conn.myPlayer.Prefix + " " + conn.myPlayer.ColourUserName + " " + conn.myPlayer.Title).Trim() + "\r\n";
                        userCount++;
                    }
                }
            }
            output += line;

            if (userCount == 1)
                output = output.Replace("{##UserCount}", "There is one person here");
            else
                output = output.Replace("{##UserCount}", "There are " + userCount.ToString() + " people here");

            sendToUser(output, true, false, false);
        }

        public void cmdHideMe(string message)
        {
            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                if (myPlayer.Invisible)
                    sendToUser("You are now visible!", true, false, false);
                else
                    sendToUser("You are now invisible!", true, false, false);
                myPlayer.Invisible = ! myPlayer.Invisible;
            }
        }

        public void cmdAway(string message)
        {
            sendToUser("You mark yourself as away", true, false, false);
            sendToRoom(myPlayer.ColourUserName + " sets " + (myPlayer.Gender == 0  ? "it's self" : (myPlayer.Gender == 1 ? "himself" : "herself")) + " as away","", false, true);
            myPlayer.Away = true;
        }

        public void cmdStaff(string message)
        {
            string[] staffList = new string[3];
            string output = "";

            foreach (Connection c in connections)
            {
                if (c.myPlayer != null && !c.myPlayer.Invisible && c.myPlayer.PlayerRank >= (int)Player.Rank.Guide && c.myPlayer.OnDuty)
                {
                    switch (c.myPlayer.PlayerRank)
                    {
                        case (int)Player.Rank.Guide:
                            staffList[0] += (staffList[0] == null ? "" : ", ") + c.myPlayer.UserName;
                            break;
                        case (int)Player.Rank.Staff:
                            staffList[1] += (staffList[1] == null ? "" : ", ") + c.myPlayer.UserName;
                            break;
                        case (int)Player.Rank.Admin:
                        case (int)Player.Rank.HCAdmin:
                            staffList[2] += (staffList[2] == null ? "" : ", ") + c.myPlayer.UserName;
                            break;
                    }
                }
            }

            if (staffList[2] != "" && staffList[2] != null)
                output = AppSettings.Default.AdminColour + AppSettings.Default.AdminName + ":{reset}\r\n" + staffList[2] + "\r\n\r\n";
            if (staffList[1] != "" && staffList[1] != null)
                output += AppSettings.Default.StaffColour + AppSettings.Default.StaffName + ":{reset}\r\n" + staffList[1] + "\r\n\r\n";
            if (staffList[0] != "" && staffList[0] != null)
                output += AppSettings.Default.GuideColour + AppSettings.Default.GuideName + ":{reset}\r\n" + staffList[0] + "\r\n\r\n";

            if (output == "")
                sendToUser("No staff online at present", true, false, false);
            else
                sendToUser("{bold}{cyan}--[{red}Staff On Line{cyan}]".PadRight(103, '-') + "{reset}\r\n" + output + "{bold}{cyan}".PadRight(92, '-') + "{reset}", true, false, false);
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
                            sendToUser("Pre: " + c.myPlayer.TotalOnlineTime.ToString());
                            sendToUser("Amount: " + amount.ToString());
                            if (increment)
                                c.myPlayer.TotalOnlineTime += amount;
                            else if (!increment && amount >= c.myPlayer.TotalOnlineTime)
                                c.myPlayer.TotalOnlineTime = 0;
                            else
                                c.myPlayer.TotalOnlineTime = c.myPlayer.TotalOnlineTime - amount;
                            sendToUser("Post: " + c.myPlayer.TotalOnlineTime.ToString());

                            sendToUser("You " + (increment ? "add " : "remove ") + alt.ToString() + " hour" + (alt > 1 ? "s " : " ") + (increment ? "to " : "from ") + c.myPlayer.UserName + "'s total time");
                            sendToUser(myPlayer.ColourUserName + " has just altered your total online time", c.myPlayer.UserName);
                            c.myPlayer.SavePlayer();
                        }
                    }
                }

            }
        }

        public void cmdSet(string message)
        {
            if (message == "")
                sendToUser("Syntax: set <jabber/icq/msn/yahoo/skype/email/hUrl/wUrl/irl/occ/home/jetlag> <value>", true, false, false);
            else
            {
                // Split the input to see if we are setting or blanking
                string[] split = message.Split(new char[] {' '}, 2, StringSplitOptions.RemoveEmptyEntries);
                switch (split[0].ToLower())
                {
                    case "jabber":
                        myPlayer.JabberAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Jabber id to " + split[1] : "You blank your Jabber id", true, false, false);
                        break;
                    case "icq":
                        myPlayer.ICQAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your ICQ id to " + split[1] : "You blank your ICQ id", true, false, false);
                        break;
                    case "msn":
                        myPlayer.MSNAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your MSN id to " + split[1] : "You blank your MSN id", true, false, false);
                        break;
                    case "yahoo":
                        myPlayer.YahooAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Yahoo id to " + split[1] : "You blank your Yahoo id", true, false, false);
                        break;
                    case "skype":
                        myPlayer.SkypeAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Skype id to " + split[1] : "You blank your Skype id", true, false, false);
                        break;
                    case "email":
                        if (split.Length == 1)
                            sendToUser("Your e-mail address visibility is set to " + (Player.ShowTo)myPlayer.EmailPermissions, true, false, false);
                        else if (split[1] == "public")
                        {
                            myPlayer.EmailPermissions = (int)Player.ShowTo.Public;
                            sendToUser("You set your e-mail visible to everyone", true, false, false);
                        }
                        else if (split[1] == "private")
                        {
                            myPlayer.EmailPermissions = (int)Player.ShowTo.Private;
                            sendToUser("You set your e-mail visible to only admin", true, false, false);
                        }
                        else if (split[1] == "friends")
                        {
                            myPlayer.EmailPermissions = (int)Player.ShowTo.Friends;
                            sendToUser("You set your e-mail visible to your friends", true, false, false);
                        }
                        else
                        {
                            if (testEmailRegex(split[1]))
                            {
                                myPlayer.EmailAddress = split[1];
                                sendToUser("You set your Email address to " + split[1], true, false, false);
                            }
                            else
                                sendToUser("Please enter a valid e-mail address", true, false, false);
                        }
                        break;
                    case "hurl":
                        myPlayer.HomeURL = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Home URL to " + split[1] : "You blank your Home URL", true, false, false);
                        break;
                    case "wurl":
                        myPlayer.WorkURL = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Work URL to " + split[1] : "You blank your Work URL", true, false, false);
                        break;
                    case "irl":
                        myPlayer.RealName = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your real name to " + split[1] : "You blank your real name", true, false, false);
                        break;
                    case "occ":
                        myPlayer.Occupation = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your occupation to " + split[1] : "You blank your occupation URL", true, false, false);
                        break;
                    case "home":
                        myPlayer.Hometown = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Home Town to " + split[1] : "You blank your Home Town", true, false, false);
                        break;
                    case "jetlag":
                        int jetlag = split.Length > 1 ? Convert.ToInt32(split[1]) : 0;
                        if (jetlag >= -12 && jetlag <= 12)
                        {
                            myPlayer.JetLag = jetlag;
                            sendToUser("You set your jetlag to " + jetlag.ToString(), true, false, false);
                        }
                        else
                            sendToUser("Maximum jetlag is +/- 12 hours", true, false, false);
                        break;
                    default:
                        sendToUser("Syntax: set <jabber/icq/msn/yahoo/skype/hUrl/wUrl/irl/occ/home/jetlag> <value>", true, false, false);
                        break;
                }
                myPlayer.SavePlayer();
            }
        }

        public void cmdDob(string message)
        {
            if (IsDate(message))
            {
                DateTime dob = Convert.ToDateTime(message);
                if (dob.Year >= DateTime.Now.Year)
                    sendToUser("Hmm .. not born yet, eh?", true, false, false);
                else
                {
                    sendToUser("You set your Date of Birth to " + dob.ToLongDateString(), true, false, false);
                    myPlayer.DateOfBirth = dob;
                    myPlayer.SavePlayer();
                }
            }
            else if (message == "" && myPlayer.DateOfBirth != DateTime.MinValue)
            {
                sendToUser("You blank your Date of Birth", true, false, false);
                myPlayer.DateOfBirth = DateTime.MinValue;
                myPlayer.SavePlayer();
            }
            else
            {
                sendToUser("Syntax: dob <dd/mm/yyyy>", true, false, false);
            }
        }

        public void cmdGender(string message)
        {
            switch (message.ToLower())
            {
                case "male":
                    myPlayer.Gender = 1;
                    sendToUser("You set your gender to male", true, false, false);
                    break;
                case "female":
                    myPlayer.Gender = 2;
                    sendToUser("You set your gender to female", true, false, false);
                    break;
                case "none":
                    myPlayer.Gender = 0;
                    sendToUser("You set your gender to neutral", true, false, false);
                    break;
                default:
                    sendToUser("Syntax: gender <male/female/none>", true, false, false);
                    break;
            }
            myPlayer.SavePlayer();
        }

        public void cmdPrompt(string message)
        {
            if (message == "" || AnsiColour.Colorise(message, true) == "")
            {
                sendToUser("You reset your prompt", true, false, false);
                myPlayer.Prompt = AppSettings.Default.TalkerName + ">";
                myPlayer.SavePlayer();
            }
            else
            {
                if (AnsiColour.Colorise(message, true).Length > 20)
                    sendToUser("Prompt too long, try again", true, false, false);
                else
                {
                    myPlayer.Prompt = message + "{reset}>";
                    sendToUser("You set your prompt to: " + myPlayer.Prompt, true, false, false);
                    myPlayer.SavePlayer();
                }
            }
        }

        public void cmdPrefix(string message)
        {
            if (message == "" || AnsiColour.Colorise(message, true) == "")
            {
                sendToUser("You remove your prefix", true, false, false);
                myPlayer.Prefix = "";
                myPlayer.SavePlayer();
            }
            else
            {
                if (AnsiColour.Colorise(message, true).Length > 10)
                    sendToUser("Prefix too long, try again", true, false, false);
                else
                {
                    myPlayer.Prefix = message + "{reset}";
                    sendToUser("You change your prefix to " + myPlayer.Prefix, true, false, false);
                    myPlayer.SavePlayer();
                }
            }
        }

        public void cmdNoPrefix(string message)
        {
            sendToUser(myPlayer.SeePrefix ? "You are now ignoring prefixes" : "You are now seeing prefixes", true, false, false);
            myPlayer.SeePrefix = !myPlayer.SeePrefix;
            myPlayer.SavePlayer();
        }

        public void cmdTitle(string message)
        {
            if (message == "" || AnsiColour.Colorise(message, true) == "")
            {
                sendToUser("You remove your title", true, false, false);
                myPlayer.Title = "";
                myPlayer.SavePlayer();
            }
            else
            {
                if (AnsiColour.Colorise(message, true).Length > 40)
                    sendToUser("Title too long, try again", true, false, false);
                else
                {
                    myPlayer.Title = message + "{reset}";
                    sendToUser("You change your title to read: " + myPlayer.Title, true, false, false);
                    myPlayer.SavePlayer();
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

        public void cmdDoList(string message)
        {
            if (message == "")
                sendToUser("Syntax: list <newbies/staff/tester/builder/players/objects" + (myPlayer.PlayerRank >= (int)Player.Rank.Staff ? "/ip/gits/rooms" : "") + ">", true, false, false);
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                string title = "";
                string output = "";
                bool found = false;
                //Player loadPlayer;
                List<Player> playerList;

                if (myPlayer.PlayerRank >= (int)Player.Rank.Staff)
                {
                    switch (split[0].Substring(0, 1))
                    {
                        case "i":
                            found = true;
                            title = "IP Addresses";
                            foreach (Connection c in connections)
                            {
                                if (c.socket.Connected && c.myPlayer != null && !c.myPlayer.Invisible)
                                    output += "\r\n" + c.myPlayer.ColourUserName.PadRight(40 + (c.myPlayer.ColourUserName.Length - c.myPlayer.UserName.Length), ' ') + ": " + c.myPlayer.CurrentIP;
                            }
                            if (output != "")
                                output = output.Substring(2);
                            break;
                        case "g":
                            found = true;
                            title = "Gits";
                            playerList = getPlayers(false, false, false, true);
                            foreach (Player p in playerList)
                            {
                                output += ", " + p.ColourUserName;
                            }
                            if (output != "") 
                                output = output.Substring(2);
                            break;
                        case "p":
                            found = true;
                            title = "Players";
                            playerList = getPlayers();
                            foreach (Player p in playerList)
                            {
                                output += ", " + p.ColourUserName;
                            }
                            if (output != "") 
                                output = output.Substring(2);
                            break;
                        case "r":
                            found = true;
                            cmdRoomList(split.Length > 1 ? split[1] : "");
                            break;
                        case "o":
                            found = true;
                            listObjects(split.Length > 1 ? split[1] : "");
                            break;
                    }       
                }
                if (!found)
                {
                    switch (split[0].Substring(0, 1))
                    {
                        case "n":
                            title = "Newbies";
                            foreach (Connection c in connections)
                            {
                                if (c.socket.Connected && c.myPlayer != null && c.myPlayer.NewPlayer)
                                    output += ", " + c.myPlayer.ColourUserName;
                            }
                            if (output != "") 
                                output = output.Substring(2);
                            break;
                        case "s":
                            string[] staffList = new string[6];
                            title = "Staff";
                            playerList = getPlayers(true, false, false, false);
                            foreach (Player p in playerList)
                            {
                                staffList[p.PlayerRank] += ", " + p.ColourUserName;
                            }
                            output += (staffList[(int)Player.Rank.HCAdmin] != null ? rankName((int)Player.Rank.HCAdmin) + ":\r\n" + staffList[(int)Player.Rank.HCAdmin].Substring(2) + "\r\n" : "");
                            output += (staffList[(int)Player.Rank.Admin] != null ? rankName((int)Player.Rank.Admin) + ":\r\n" + staffList[(int)Player.Rank.Admin].Substring(2) + "\r\n" : "");
                            output += (staffList[(int)Player.Rank.Staff] != null ? rankName((int)Player.Rank.Staff) + ":\r\n" + staffList[(int)Player.Rank.Staff].Substring(2) + "\r\n" : "");
                            output += (staffList[(int)Player.Rank.Guide] != null ? rankName((int)Player.Rank.Guide) + ":\r\n" + staffList[(int)Player.Rank.Guide].Substring(2) + "\r\n" : "");
                            break;
                        case "t":
                            title = "Testers";
                            playerList = getPlayers(false, false, true, false);
                            foreach (Player p in playerList)
                            {
                                output += ", " + p.ColourUserName;
                            }
                            if (output != "") 
                                output = output.Substring(2);
                            break;
                        case "b":
                            title = "Builders";
                            playerList = getPlayers(false, true, false, false);
                            foreach (Player p in playerList)
                            {
                                output += ", " + p.ColourUserName;
                            }
                            if (output != "") 
                                output = output.Substring(2);
                            break;
                        default:
                            sendToUser("Syntax: list <newbies/staff/tester/builder/players/objects" + (myPlayer.PlayerRank >= (int)Player.Rank.Staff ? "/ip/gits/rooms" : "") + ">", true, false, false);
                            break;
                    }
                }
                if (output != "" || title != "")
                {
                    string header = "{bold}{cyan}---[{reset}" + title + "{bold}{cyan}]";
                    string headerNoCol = AnsiColour.Colorise(header, true);

                    sendToUser(headerNoCol.PadRight(80, '-').Replace(headerNoCol, header) + "{reset}\r\n" + (output == "" ? "None found" : output) + "\r\n{bold}{cyan}" + "{reset}".PadLeft(87, '-'), true, false, false);
                }
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
                        sendToUser("{bold}{cyan}{bell}>>> {blink}" + myPlayer.UserName + " announces \"" + message.Substring(1).Trim() + "\"{reset}{bold}{cyan} <<{reset}", c.myPlayer.UserName, true, c.myPlayer.DoColour, !(c.myPlayer.UserName==myPlayer.UserName), true);
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
                        if (c.socket.Connected && c.myPlayer!= null && c.myPlayer.UserName.ToLower() == target[0])
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

        public void cmdSave(string message)
        {
            if (message.ToLower() == "all" && myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null)
                        c.myPlayer.SavePlayer();
                }
                sendToUser("All active players saved", true, false, false);
            }
            else
            {
                sendToUser(myPlayer.SavePlayer() ? "Player file saved" : "Sorry, there is a problem saving your profile", true, false, false);
            }
        }

        public void cmdLock(string message)
        {
            if (message == "")
                sendToUser("Current locks: " + (AppSettings.Default.LockLevel > 0 ? ((Player.Rank)AppSettings.Default.LockLevel).ToString() : "None"), true, false, false);
            else
            {
                switch (message)
                {
                    case "0":
                    case "n":
                        AppSettings.Default.LockLevel = 0;
                        AppSettings.Default.Save();
                        break;
                    case "1":
                    case "r":
                        AppSettings.Default.LockLevel = 1;
                        AppSettings.Default.Save();
                        break;
                    case "2":
                    case "g":
                        AppSettings.Default.LockLevel = 2;
                        AppSettings.Default.Save();
                        break;
                    case "3":
                    case "s":
                        AppSettings.Default.LockLevel = 3;
                        AppSettings.Default.Save();
                        break;
                    case "4":
                    case "a":
                        AppSettings.Default.LockLevel = 4;
                        AppSettings.Default.Save();
                        break;
                    case "5":
                    case "h":
                        AppSettings.Default.LockLevel = 5;
                        AppSettings.Default.Save();
                        break;
                    default:
                        sendToUser("Syntax: lock <0/1/2/3/4/5/n/r/g/s/a/h>", true, false, false);
                        break;
                }
                sendToUser("Current locks: " + (AppSettings.Default.LockLevel > 0 ? ((Player.Rank)AppSettings.Default.LockLevel).ToString() : "None"), true, false, false);
            }
        }

        public void cmdHistory(string message)
        {
            string output = "{bold}{cyan}---[{red}History{cyan}]".PadRight(103, '-') + "\r\n{reset}";
            if (history.Count > 0)
            {
                List<string> temp = history;
                temp.Reverse();
                for (int i = temp.Count; i > 0; i--)
                {
                    output += "{bold}{cyan}" + (i < 10 ? "0" : "") + i.ToString() + "{reset} " + temp[i-1] + "\r\n";
                }
            }
            else
            {
                output += "No messages in history buffer\r\n";
            }
            output += "{bold}{cyan}" + "" .PadRight(80,'-') + "{reset}\r\n";
            sendToUser(output, false, false, false);
        }

        public void cmdHelp(string message)
        {
            string helpfile = null;
            if (message == "")
                helpfile = AnsiColour.Colorise(loadTextFile(@"help" + Path.DirectorySeparatorChar + "help.txt"));
            else
                helpfile = AnsiColour.Colorise(loadTextFile(@"help" + Path.DirectorySeparatorChar + message.ToLower() + ".txt"));

            if (helpfile == "" || helpfile == null || helpfile.Length < 5)
            {
                sendToUser("No help found for topic \"" + message + "\"", true, false, false);
                logToFile(myPlayer.UserName + " just asked for help on \"" + message + "\" - not found", "help");
            }
            else if (Convert.ToInt32(helpfile.Substring(0, 1)) > myPlayer.PlayerRank)
            {
                sendToUser("No help found for topic \"" + message + "\"", true, false, false);
            }
            else
            {
                sendToUser(("{bold}{cyan}---[{red}Help" + (message != "" ? ": \"" + message + "\"" : "") + "{cyan}]").PadRight(103, '-') + "\r\n{reset}" + helpfile.Substring(1) + "\r\n{bold}{cyan}" + "".PadRight(80, '-') + "{reset}", true, false, false);
            }
        }

        public void cmdGrant(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: Grant <player> <admin/staff/guide/noidle/tester/builder>", true, false, false);
            else
            {
                string[] split = message.Split(new char[] {' '}, 2);
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("Player \"" + split[0] + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("You cannot grant privs to yourself!", true, false, false);
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
                        switch (split[1].Substring(0, 1).ToLower())
                        {
                            case "a":
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
                            case "s":
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
                            case "g":
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
                            case "n":
                                // Granting noidle
                                sendToUser("You " + (t.SpecialPrivs.noidle ? "remove" : "grant") + " idle protection to " + t.UserName, true, false, false);
                                if (online) sendToUser(myPlayer.UserName + " has " + (t.SpecialPrivs.noidle ? "removed your" : "granted you") + " idle protection", true, false, false);
                                logToFile(myPlayer.UserName + " has just " + (t.SpecialPrivs.noidle ? "removed" : "granted") + " idle protection to " + t.UserName, "grant");
                                p = t.SpecialPrivs;
                                p.noidle = !p.noidle;
                                t.SpecialPrivs = p;
                                break;
                            case "b":
                                // Granting builder
                                sendToUser("You " + (t.SpecialPrivs.builder ? "remove" : "grant") + " builder privs to " + t.UserName, true, false, false);
                                logToFile(myPlayer.UserName + " has just " + (t.SpecialPrivs.builder ? "removed" : "granted") + " builder privs to " + t.UserName, "grant");
                                if (online) sendToUser(myPlayer.UserName + " has " + (t.SpecialPrivs.builder ? "removed your" : "granted you") + " builder privs", true, false, false);
                                p = t.SpecialPrivs;
                                p.builder = !p.builder;
                                t.SpecialPrivs = p;
                                break;
                            case "t":
                                // Granting builder
                                sendToUser("You " + (t.SpecialPrivs.tester ? "remove" : "grant") + " tester privs to " + t.UserName, true, false, false);
                                logToFile(myPlayer.UserName + " has just " + (t.SpecialPrivs.tester ? "removed" : "granted") + " tester privs to " + t.UserName, "grant");
                                if (online) sendToUser(myPlayer.UserName + " has " + (t.SpecialPrivs.tester ? "removed your" : "granted you") + " tester privs", true, false, false);
                                p = t.SpecialPrivs;
                                p.tester = !p.tester;
                                t.SpecialPrivs = p;
                                break;
                            default:
                                sendToUser("Syntax: Grant <player> <admin/staff/guide/noidle/tester/builder>", true, false, false);
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

        public void cmdBug(string message)
        {
            if (!myPlayer.SpecialPrivs.tester)
                sendToUser("You need tester privs to use this command", true, false, false);
            else if (message == "")
                sendToUser("Syntax: Bug <bug info>", true, false, false);
            else
            {
                logToFile("From: " + myPlayer.UserName + " : " + message, "bug");
                sendToUser("Bug logged. Thank you", true, false, false);
            }
        }

        public void cmdAbuse(string message)
        {
            List<string> temp = history;
            temp.Reverse();
            for(int i = 0; i < temp.Count; i++)
            {
                logToFile("[" + myPlayer.UserName + "] " + AnsiColour.Colorise(temp[i], true), "abuse");
            }
            sendToUser("Your history has been sent to the admin as an abuse report", true, true, false);
            sendToStaff(myPlayer.UserName + " has just logged an abuse report", (int)Player.Rank.Admin, true);
        }

        public void cmdResCount(string message)
        {
            List<Player> playerList = getPlayers();
            sendToUser("There " + (playerList.Count == 1 ? "is one resident" : "are " + playerList.Count.ToString() + " residents"), true, false, false);
        }

        public void cmdEDump(string message)
        {
            List<Player> playerList = getPlayers((message.ToLower() == "staff"), false, false, false);
            string path = Path.Combine(Server.userFilePath,(@"dump" + Path.DirectorySeparatorChar));
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
                count ++;
            }

            sw.Flush();
            sw.Close();
            sendToUser(count.ToString() + " e-mail address" + (count == 1 ? "" : "es") + " dumped to file " + path + "emaillist.txt", true, false, false);
        }

        public void cmdColour(string message)
        {
            if (message == "")
                sendToUser("Colour is set to: " + (myPlayer.DoColour ? "On" : "Off"), true, false, false);
            else
            {
                switch (message.ToLower())
                {
                    case "on":
                        myPlayer.DoColour = true;
                        sendToUser("Colour is set to: On", true, false, false);
                        myPlayer.SavePlayer();
                        break;
                    case "off":
                        myPlayer.DoColour = false;
                        sendToUser("Colour is set to: Off", true, false, false);
                        myPlayer.SavePlayer();
                        break;
                    default:
                        sendToUser("Syntax: Colour <ON/OFF>", true, false, false);
                        break;
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
                    for(int i = 0; i < messages.Count; i++)
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
                                string path = Path.Combine(Server.userFilePath,("rooms" + Path.DirectorySeparatorChar + r.systemName.ToLower() + ".xml"));

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

        public void cmdITag(string message)
        {
            if (message == "")
                sendToUser("Syntax: itag <player> <inform tag>", true, false, false);
            else
            {
                string[] target = (message.IndexOf(" ") > -1 ? matchPartial(message.Split(new char[] { ' ' }, 2)[0]) : matchPartial(message));
                string tag = (message.IndexOf(" ") > -1 ? message.Split(new char[] { ' ' }, 2)[1] : "");

                if (target.Length == 0)
                    sendToUser("Player \"" + (message.IndexOf(" ") > -1 ? message.Split(new char[] { ' ' }, 2)[0] : message) + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online at the moment", true, false, false);
                else
                {
                    if (target[0] == myPlayer.UserName)
                    {
                        sendToUser(tag == "" ? "You remove your own inform tag" : "You set your inform tag to: " + tag, true, false, false);
                        myPlayer.InformTag = tag;
                        myPlayer.SavePlayer();
                    }
                    else
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName == target[0])
                            {
                                sendToUser(tag == "" ? "You remove " + c.myPlayer.UserName + "'s inform tag" : "You set " + c.myPlayer.UserName + "'s inform tag to: " + tag, true, false, false);
                                if (!c.myPlayer.InEditor)
                                    c.sendToUser("\r\n" + myPlayer.ColourUserName +  (tag == "" ? " has just removed your inform tag" : " has just set your inform tag to: " + tag), true, false, false);

                                c.myPlayer.InformTag = tag;
                                c.myPlayer.SavePlayer();
                            }
                        }
                    }
                }
            }
        }

        public void cmdRecap(string message)
        {
            if (message == "")
                sendToUser("Syntax: recap " + (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? "<player> " : "") + "<re-capped name>",true, false, false);
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

        public void cmdLogonMsg(string message)
        {
            if (message == "" && myPlayer.LogonMsg == "")
                sendToUser("Syntax: logonmsg <message>", true, false, false);
            else if (message == "" && myPlayer.LogonMsg != "")
            {
                myPlayer.LogonMsg = "";
                myPlayer.SavePlayer();
                sendToUser("Logon message blanked", true, false, false);
            }
            else
            {
                myPlayer.LogonMsg = message;
                myPlayer.SavePlayer();
                sendToUser("You set your logon message to: " + myPlayer.ColourUserName + " " + message, true, false, false);
            }
        }

        public void cmdLogoffMsg(string message)
        {
            if (message == "" && myPlayer.LogoffMsg == "")
                sendToUser("Syntax: logoffmsg <message>", true, false, false);
            else if (message == "" && myPlayer.LogoffMsg != "")
            {
                myPlayer.LogoffMsg = "";
                myPlayer.SavePlayer();
                sendToUser("Logoff message blanked", true, false, false);
            }
            else
            {
                myPlayer.LogoffMsg = message;
                myPlayer.SavePlayer();
                sendToUser("You set your logoff message to: " + myPlayer.ColourUserName + " " + message, true, false, false);
            }
        }

        public void cmdEnterMsg(string message)
        {
            if (message == "" && myPlayer.EnterMsg == "")
                sendToUser("Syntax: entermsg <message>", true, false, false);
            else if (message == "" && myPlayer.EnterMsg != "")
            {
                myPlayer.EnterMsg = "";
                myPlayer.SavePlayer();
                sendToUser("Enter message blanked", true, false, false);
            }
            else
            {
                myPlayer.EnterMsg = message;
                myPlayer.SavePlayer();
                sendToUser("You set your enter message to: " + myPlayer.ColourUserName + " " + message, true, false, false);
            }
        }

        public void cmdExitMsg(string message)
        {
            if (message == "" && myPlayer.ExitMsg == "")
                sendToUser("Syntax: exitmsg <message>", true, false, false);
            else if (message == "" && myPlayer.ExitMsg != "")
            {
                myPlayer.ExitMsg = "";
                myPlayer.SavePlayer();
                sendToUser("Exit message blanked", true, false, false);
            }
            else
            {
                myPlayer.ExitMsg = message;
                myPlayer.SavePlayer();
                sendToUser("You set your exit message to: " + myPlayer.ColourUserName + " " + message, true, false, false);
            }
        }

        public void cmdPinfo(string message)
        {
            string[] target = matchPartial(message == "" ? myPlayer.UserName : (myPlayer.PlayerRank >= (int)Player.Rank.Staff ? message : myPlayer.UserName));
            if (target.Length == 0)
                sendToUser("Player \"" + message + "\" not found", true, false, false);
            else if (target.Length > 1)
                sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
            else
            {
                Player p = Player.LoadPlayer(target[0], 0);
                string output = headerLine("Player Info: " + p.ColourUserName) + "\r\n";
                output += "{bold}{blue}  Logon Message{reset}: " + p.LogonMsg + "\r\n";
                output += "{bold}{blue} Logoff Message{reset}: " + p.LogoffMsg + "\r\n";
                output += "{bold}{blue}  Enter Message{reset}: " + p.EnterMsg + "\r\n";
                output += "{bold}{blue}   Exit Message{reset}: " + p.ExitMsg + "\r\n";
                output += footerLine();
                sendToUser(output, true, false, false);
            }
        }

        public void cmdList(string message)
        {
            if (message == "")
            {
                string output = headerLine("List") + "\r\n{bold}{blue}"
                    + "Name".PadRight(32)
                    + "Frd".PadRight(4)
                    + "Fnd".PadRight(4)
                    + "Inf".PadRight(4)
                    + "Noi".PadRight(4)
                    + "Ign".PadRight(4)
                    + "Bar".PadRight(4)
                    + "Bee".PadRight(4)
                    + "Blo".PadRight(4)
                    + "MBl".PadRight(4)
                    + "Grb".PadRight(4)
                    + "Key"
                    + "{reset}\r\n";


                List<Player.playerList> myList = myPlayer.MyList;
                myList.Sort(delegate(Player.playerList p1, Player.playerList p2) { return p1.name.CompareTo(p2.name); });
                foreach (Player.playerList p in myList)
                {
                    Player t = Player.LoadPlayer(p.name, 0);
                    output += (t.UserName.PadRight(33)).Replace(t.UserName, t.ColourUserName)
                        + "{bold}{white}"
                        + (p.friend ? "Y |" : "N |").PadRight(4)
                        + (p.find ? "Y |" : "N |").PadRight(4)
                        + (p.inform ? "Y |" : "N |").PadRight(4)
                        + (p.noisy ? "Y |" : "N |").PadRight(4)
                        + (p.ignore ? "Y |" : "N |").PadRight(4)
                        + (p.bar ? "Y |" : "N |").PadRight(4)
                        + (p.beep ? "Y |" : "N |").PadRight(4)
                        + (p.block ? "Y |" : "N |").PadRight(4)
                        + (p.mailblock ? "Y |" : "N |").PadRight(4)
                        + (p.grabme ? "Y |" : "N |").PadRight(4)
                        + (p.key ? "Y" : "N")
                        + "\r\n{reset}";
                }
                output += footerLine() + "\r\n";
                Player.playerList pl = myPlayer.allPlayersList;
                output += "All".PadRight(33)
                    + "{bold}{white}"
                    + (pl.friend ? "Y |" : "N |").PadRight(4)
                    + (pl.find ? "Y |" : "N |").PadRight(4)
                    + (pl.inform ? "Y |" : "N |").PadRight(4)
                    + (pl.noisy ? "Y |" : "N |").PadRight(4)
                    + (pl.ignore ? "Y |" : "N |").PadRight(4)
                    + (pl.bar ? "Y |" : "N |").PadRight(4)
                    + (pl.beep ? "Y |" : "N |").PadRight(4)
                    + (pl.block ? "Y |" : "N |").PadRight(4)
                    + (pl.mailblock ? "Y |" : "N |").PadRight(4)
                    + (pl.grabme ? "Y |" : "N |").PadRight(4)
                    + (pl.key ? "Y" : "N")
                    + "\r\n{reset}";
                pl = myPlayer.allFriendsList;
                output += "Friends".PadRight(33)
                    + "{bold}{white}"
                    + (pl.friend ? "Y |" : "N |").PadRight(4)
                    + (pl.find ? "Y |" : "N |").PadRight(4)
                    + (pl.inform ? "Y |" : "N |").PadRight(4)
                    + (pl.noisy ? "Y |" : "N |").PadRight(4)
                    + (pl.ignore ? "Y |" : "N |").PadRight(4)
                    + (pl.bar ? "Y |" : "N |").PadRight(4)
                    + (pl.beep ? "Y |" : "N |").PadRight(4)
                    + (pl.block ? "Y |" : "N |").PadRight(4)
                    + (pl.mailblock ? "Y |" : "N |").PadRight(4)
                    + (pl.grabme ? "Y |" : "N |").PadRight(4)
                    + (pl.key ? "Y" : "N")
                    + "\r\n{reset}";
                pl = myPlayer.allStaffList;
                output += "Staff".PadRight(33)
                    + "{bold}{white}"
                    + (pl.friend ? "Y |" : "N |").PadRight(4)
                    + (pl.find ? "Y |" : "N |").PadRight(4)
                    + (pl.inform ? "Y |" : "N |").PadRight(4)
                    + (pl.noisy ? "Y |" : "N |").PadRight(4)
                    + (pl.ignore ? "Y |" : "N |").PadRight(4)
                    + (pl.bar ? "Y |" : "N |").PadRight(4)
                    + (pl.beep ? "Y |" : "N |").PadRight(4)
                    + (pl.block ? "Y |" : "N |").PadRight(4)
                    + (pl.mailblock ? "Y |" : "N |").PadRight(4)
                    + (pl.grabme ? "Y |" : "N |").PadRight(4)
                    + (pl.key ? "Y" : "N")
                    + "\r\n{reset}";
                output += footerLine();
                sendToUser(output, true, false, false);
            }
            else
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                if (split.Length < 2)
                    sendToUser("syntax: list <playername/all/staff/friends> <find/inform/noisy/ignore/bar/beep/block/mblock/grab/key>", true, false, false);
                else
                {
                    if (split[0].ToLower() == "all")
                    {
                        switch (split[1].ToLower())
                        {
                            case "find":
                                myPlayer.allPlayersList.find = !myPlayer.allPlayersList.find;
                                sendToUser("You " + (myPlayer.allPlayersList.find ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "inform":
                                myPlayer.allPlayersList.inform = !myPlayer.allPlayersList.inform;
                                sendToUser("You " + (myPlayer.allPlayersList.inform ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "noisy":
                                myPlayer.allPlayersList.noisy = !myPlayer.allPlayersList.noisy;
                                sendToUser("You " + (myPlayer.allPlayersList.noisy ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "ignore":
                                myPlayer.allPlayersList.ignore = !myPlayer.allPlayersList.ignore;
                                sendToUser("You " + (myPlayer.allPlayersList.ignore ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "bar":
                                myPlayer.allPlayersList.bar = !myPlayer.allPlayersList.bar;
                                sendToUser("You " + (myPlayer.allPlayersList.bar ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "beep":
                                myPlayer.allPlayersList.beep = !myPlayer.allPlayersList.beep;
                                sendToUser("You " + (myPlayer.allPlayersList.beep ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "block":
                                myPlayer.allPlayersList.block = !myPlayer.allPlayersList.block;
                                sendToUser("You " + (myPlayer.allPlayersList.block ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "mblock":
                                myPlayer.allPlayersList.mailblock = !myPlayer.allPlayersList.mailblock;
                                sendToUser("You " + (myPlayer.allPlayersList.mailblock ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "grab":
                                myPlayer.allPlayersList.grabme = !myPlayer.allPlayersList.grabme;
                                sendToUser("You " + (myPlayer.allPlayersList.grabme ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "key":
                                myPlayer.allPlayersList.key = !myPlayer.allPlayersList.key;
                                sendToUser("You " + (myPlayer.allPlayersList.key ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            default:
                                sendToUser("syntax: list <playername/all/staff/friends> <find/inform/noisy/ignore/bar/beep/block/mblock/grab/key>", true, false, false);
                                break;
                        }
                    }
                    else if (split[0].ToLower() == "friends")
                    {
                        switch (split[1].ToLower())
                        {
                            case "find":
                                myPlayer.allFriendsList.find = !myPlayer.allFriendsList.find;
                                sendToUser("You " + (myPlayer.allFriendsList.find ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "inform":
                                myPlayer.allFriendsList.inform = !myPlayer.allFriendsList.inform;
                                sendToUser("You " + (myPlayer.allFriendsList.inform ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "noisy":
                                myPlayer.allFriendsList.noisy = !myPlayer.allFriendsList.noisy;
                                sendToUser("You " + (myPlayer.allFriendsList.noisy ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "ignore":
                                myPlayer.allFriendsList.ignore = !myPlayer.allFriendsList.ignore;
                                sendToUser("You " + (myPlayer.allFriendsList.ignore ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "bar":
                                myPlayer.allFriendsList.bar = !myPlayer.allFriendsList.bar;
                                sendToUser("You " + (myPlayer.allFriendsList.bar ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "beep":
                                myPlayer.allFriendsList.beep = !myPlayer.allFriendsList.beep;
                                sendToUser("You " + (myPlayer.allFriendsList.beep ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "block":
                                myPlayer.allFriendsList.block = !myPlayer.allFriendsList.block;
                                sendToUser("You " + (myPlayer.allFriendsList.block ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "mblock":
                                myPlayer.allFriendsList.mailblock = !myPlayer.allFriendsList.mailblock;
                                sendToUser("You " + (myPlayer.allFriendsList.mailblock ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "grab":
                                myPlayer.allFriendsList.grabme = !myPlayer.allFriendsList.grabme;
                                sendToUser("You " + (myPlayer.allFriendsList.grabme ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "key":
                                myPlayer.allFriendsList.key = !myPlayer.allFriendsList.key;
                                sendToUser("You " + (myPlayer.allFriendsList.key ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            default:
                                sendToUser("syntax: list <playername/all/staff/friends> <find/inform/noisy/ignore/bar/beep/block/mblock/grab/key>", true, false, false);
                                break;
                        }
                    }
                    else if (split[0].ToLower() == "staff")
                    {
                        switch (split[1].ToLower())
                        {
                            case "find":
                                myPlayer.allStaffList.find = !myPlayer.allStaffList.find;
                                sendToUser("You " + (myPlayer.allStaffList.find ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "inform":
                                myPlayer.allStaffList.inform = !myPlayer.allStaffList.inform;
                                sendToUser("You " + (myPlayer.allStaffList.inform ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "noisy":
                                myPlayer.allStaffList.noisy = !myPlayer.allStaffList.noisy;
                                sendToUser("You " + (myPlayer.allStaffList.noisy ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "ignore":
                                myPlayer.allStaffList.ignore = !myPlayer.allStaffList.ignore;
                                sendToUser("You " + (myPlayer.allStaffList.ignore ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "bar":
                                myPlayer.allStaffList.bar = !myPlayer.allStaffList.bar;
                                sendToUser("You " + (myPlayer.allStaffList.bar ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "beep":
                                myPlayer.allStaffList.beep = !myPlayer.allStaffList.beep;
                                sendToUser("You " + (myPlayer.allStaffList.beep ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "block":
                                myPlayer.allStaffList.block = !myPlayer.allStaffList.block;
                                sendToUser("You " + (myPlayer.allStaffList.block ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "mblock":
                                myPlayer.allStaffList.mailblock = !myPlayer.allStaffList.mailblock;
                                sendToUser("You " + (myPlayer.allStaffList.mailblock ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "grab":
                                myPlayer.allStaffList.grabme = !myPlayer.allStaffList.grabme;
                                sendToUser("You " + (myPlayer.allStaffList.grabme ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            case "key":
                                myPlayer.allStaffList.key = !myPlayer.allStaffList.key;
                                sendToUser("You " + (myPlayer.allStaffList.key ? "set" : "remove") + " the " + split[1].ToLower() + " flag for all players", true, false, false);
                                break;
                            default:
                                sendToUser("syntax: list <playername/all/staff/friends> <find/inform/noisy/ignore/bar/beep/block/mblock/grab/key>", true, false, false);
                                break;
                        }
                    }
                    else
                    {
                        string[] target = matchPartial(split[0]);
                        if (target.Length == 0)
                            sendToUser("Player \"" + split[0] + "\" not found", true, false, false);
                        else if (target.Length > 1)
                            sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                        else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                            sendToUser("You cannot add yourself to the list!", true, false, false);
                        else
                        {
                            int result;
                            switch (split[1].ToLower())
                            {
                                case "find":
                                case "inform":
                                case "noisy":
                                case "ignore":
                                case "bar":
                                case "beep":
                                case "block":
                                case "mblock":
                                case "grab":
                                case "key":
                                    result = myPlayer.UpdateList(target[0], split[1].ToLower());
                                    if (result == -1)
                                        sendToUser("Sorry, something has gone wrong ... ", true, false, false);
                                    else
                                        sendToUser("You " + (result == 0 ? "remove" : "set") + " the " + split[1] + " flag for " + target[0], true, false, false);
                                    break;
                                default:
                                    sendToUser("syntax: list <playername/all/staff/friends> <find/inform/noisy/ignore/bar/beep/block/mblock/grab>", true, false, false);
                                    break;
                            }
                        }
                    }
                }
            }
        }

        public void cmdLogonScript(string message)
        {
            if (message == "" && myPlayer.LogonScript == "")
                sendToUser("Syntax: logonscript <command(s);>", true, false, false);
            else if (message == "")
            {
                sendToUser("You remove your logon script", true, false, false);
                myPlayer.LogonScript = "";
                myPlayer.SavePlayer();
            }
            else
            {
                sendToUser("Logon script set", true, false, false);
                myPlayer.LogonScript = message;
                myPlayer.SavePlayer();
            }
        }

        public void cmdQuit(string message)
        {
            Writer.WriteLine(AnsiColour.Colorise("Thanks for visiting &t. Goodbye", myPlayer.DoColour));
            Writer.Flush();
            
            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Logout: " + myPlayer.UserName);

            if (myPlayer.LogoffMsg != "")
                sendToRoom(myPlayer.UserName + " " + myPlayer.LogoffMsg, null);
            else
                sendToRoom(myPlayer.UserName + " leaves for normality", null);

            myPlayer.TotalOnlineTime += Convert.ToInt16((DateTime.Now - myPlayer.CurrentLogon).TotalSeconds);
            myPlayer.LastLogon = DateTime.Now;
            myPlayer.LastIP = myPlayer.CurrentIP;
            int longCheck = (int)(DateTime.Now - myPlayer.CurrentLogon).TotalSeconds;
            if (longCheck > myPlayer.LongestLogin) myPlayer.LongestLogin = longCheck;

            myPlayer.SavePlayer();

            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }
        }

        public void cmdDescription(string message)
        {
            if (message == "")
            {
                myPlayer.InDescriptionEditor = true;
                sendToUser("Now entering description editor. Type \".help\" for a list of editor commands", true, false, false);
                editText = myPlayer.Description;
            }
            else
            {
                if (message == "me")
                    message = myPlayer.UserName;
                string[] target = matchPartial(message);
                if (target.Length == 0)
                sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else
                {
                    Player targ = Player.LoadPlayer(target[0],0);
                    sendToUser(headerLine("Description: " + targ.UserName) + "\r\n" + targ.Description + "\r\n" + footerLine(), true, false, false);
                }
            }
        }

        public void cmdTagline(string message)
        {
            if (message == "" && myPlayer.Tagline == "")
            {
                sendToUser("Syntax: tagline <text>", true, false, false);
            }
            else if (message == "")
            {
                myPlayer.Tagline = "";
                myPlayer.SavePlayer();
                sendToUser("You blank your tagline", true, false, false);
            }
            else if (AnsiColour.Colorise(message, true).Length > 160)
            {
                sendToUser("Message too long - try again", true, false, false);
            }
            else
            {
                myPlayer.Tagline = message;
                myPlayer.SavePlayer();
                sendToUser("You set your tagline to: " + myPlayer.Tagline, true, false, false);
            }
        }

        public void cmdInform(string message)
        {
            if (message == "")
            {
                string output = "";
                int tabCount = 1;
                foreach (Player.playerList p in myPlayer.MyList)
                {
                    if (p.inform)
                    {
                        output += p.name.PadRight(20) + (tabCount++ % 4 == 0 ? "\r\n" : "");
                    }
                }
                if (output == "")
                    output = "No inform players set";

                output = "\r\nInform All set to " + (myPlayer.InformAll ? "On" : "Off") + "\r\n" + "Inform Friends set to " + (myPlayer.InformFriends ? "On" : "Off") + "\r\n" + footerLine() + "\r\n" + output + "\r\n";
                sendToUser(headerLine("Inform") + output + footerLine(), true, false, false);
            }
            else if (message.ToLower() == "all")
            {
                myPlayer.InformAll = !myPlayer.InformAll;
                sendToUser("Inform All set to " + (myPlayer.InformAll ? "On" : "Off"), true, false, false);
                myPlayer.SavePlayer();
            }
            else if (message.ToLower() == "friends")
            {
                myPlayer.InformFriends = !myPlayer.InformFriends;
                sendToUser("Inform Friends set to " + (myPlayer.InformFriends ? "On" : "Off"), true, false, false);
                myPlayer.SavePlayer();
            }
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("No such player \"" + message + "\"", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target.Length == 1 && (target[0].ToLower() == myPlayer.UserName.ToLower()))
                    sendToUser("You can't add yourself to your own inform list!", true, false, false);
                else
                {
                    bool found = false;
                    foreach (Player.playerList p in myPlayer.MyList)
                    {
                        if (p.name.ToLower() == target[0].ToLower())
                        {
                            found = true;
                        }
                    }
                    if (!found)
                    {
                        if (myPlayer.SetInform(target[0]))
                            sendToUser(target[0] + " added to your inform list", true, false, false);
                        else
                            sendToUser("No more space in your list", true, false, false);
                    }
                    else
                    {
                        if (myPlayer.InformFor(target[0]))
                        {
                            myPlayer.RemoveInform(target[0]);
                            sendToUser(target[0] + " removed from your inform list", true, false, false);
                        }
                        else
                        {
                            myPlayer.SetInform(target[0]);
                            sendToUser(target[0] + " added to your inform list", true, false, false);
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

        public void cmdKick(string message)
        {
            if (message == "")
                sendToUser("Syntax: kick <player name> [reason]", true, false, false);
            else
            {
                string[] split = message.Split(new char[] {' '},2);
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

                    // Now need to kill the user file
                    Player.RemovePlayerFile(target[0]);
                    logToFile("[Nuke] Player \"" + target[0] + "\" has just been killed by " + myPlayer.UserName, "admin");
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
                            output += s.Replace("|","") + "^N\r\n";
                    }
                }
            }
            sendToUser(headerLine("Last") + "\r\n" + output + footerLine(), true, false, false);
        }

        public void cmdHChime(string message)
        {
            myPlayer.HourlyChime = !myPlayer.HourlyChime;
            sendToUser("You turn hourly chime notifications " + (myPlayer.HourlyChime ? "on" : "off"),true, false, false);
            myPlayer.SavePlayer();
        }

        public void cmdPassword(string message)
        {
            string[] split = message.Split(new char[] { ' ' });
            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin && message != "" && split.Length > 1)
            {
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("No such user \"" + split[0] + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Please use the password command without a name to reset your password", true, false, false);
                else
                {
                    Player targ = Player.LoadPlayer(target[0], 0);
                    targ.Password = split[1];
                    targ.SavePlayer();
                    sendToUser("Password updated for player \"" + targ.ColourUserName + "\"", true, false, false);
                }
            }
            else if (myPlayer.PlayerRank >= (int)Player.Rank.Admin && split.Length < 2)
                sendToUser("Syntax: password <playername> <new password>", true, false, false);
            else
            {
                myState = 11;
                sendEchoOff();
                sendToUser("Please enter your current password: ", false, false, false);
            }
        }

        public void cmdStats(string message)
        {
            if (message == "")
                sendToUser("Syntax: stats <all/player/command>", true, false, false);
            else
            {
                string output = "";

                roomList = loadRooms();

                switch (message.ToLower())
                {
                    case "all":
                        PerformanceCounter pc = new PerformanceCounter("Memory", "Available Bytes");
                        long freeMemory = Convert.ToInt64(pc.NextValue());

                        Process proc = Process.GetCurrentProcess();
                        long memused = proc.PrivateMemorySize64;

                        output += centerText(AppSettings.Default.TalkerName + " has been up for " + formatTime(DateTime.Now - Server.startTime) + "\r\n");
                        output += centerText("There " + (Server.playerCount == 1 ? "has " : "have ") + "been " + Server.playerCount + " player" + (Server.playerCount == 1 ? "" : "s") + " connected in that time") + "\r\n";
                        output += centerText("There are " + cmds.Count.ToString() + " player commands and " + roomList.Count.ToString() + " system room" + (roomCount() > 1 ? "s" : "")) + "\r\n";
                        output += centerText("There are currently " + mail.Count.ToString() + " mail item" + (mail.Count == 1 ? "" : "s") + " on the server") + "\r\n";
                        output += centerText("The local time for " + AppSettings.Default.TalkerName + " is " + DateTime.Now.ToShortTimeString()) + "\r\n";
                        output += centerText(AppSettings.Default.TalkerName + " has " + (freeMemory / 1048576).ToString() + "Mb free memory, and is using " + (memused / 1048576).ToString() + "Mb") + "\r\n";
                        sendToUser(headerLine("Stats: " + message.ToLower()) + "\r\n" + output + footerLine(), true, false, false);
                        break;
                    case "player":
                        List<Player> playerList = getPlayers();
                        output += centerText(AppSettings.Default.TalkerName + " has " + playerList.Count.ToString() + " resident" + (playerList.Count == 1 ? "" : "s")) + "\r\n";
                        output += centerText(AppSettings.Default.TalkerName + " has been up for " + formatTime(DateTime.Now - Server.startTime) + "\r\n");
                        output += centerText("There " + (Server.playerCount == 1 ? "has " : "have ") + "been " + Server.playerCount + " player" + (Server.playerCount == 1 ? "" : "s") + " connected in that time") + "\r\n";
                        int pph = (int)(Server.playerCount / (DateTime.Now - Server.startTime).TotalHours);
                        output += centerText("At an average of " + pph.ToString() + " players per hour") + "\r\n";
                        sendToUser(headerLine("Stats: " + message.ToLower()) + "\r\n" + output + footerLine(), true, false, false);
                        break;
                    case "command":
                        int tabCount = 0;
                        int cmdTotalCount = 0;
                        foreach (commands c in cmds)
                        {
                            string temp = c.cmdText + ": " + Server.cmdUseCount(c.cmdText);
                            cmdTotalCount += Server.cmdUseCount(c.cmdText);

                            output += temp.PadLeft(20);
                            if (++tabCount % 3 == 0)
                                output += "\r\n";
                                
                        }
                        output += "\r\n" + centerText(AppSettings.Default.TalkerName + " has been up for " + formatTime(DateTime.Now - Server.startTime) + "\r\n");
                        output += centerText(cmdTotalCount.ToString() + " commands used at an average of " + ((int)(cmdTotalCount / (DateTime.Now - Server.startTime).TotalMinutes)).ToString() + " commands per minute") + "\r\n";
                        sendToUser(headerLine("Stats: " + message.ToLower()) + "\r\n" + output + footerLine(), true, false, false);
                        break;
                    default:
                        sendToUser("Syntax: stats <all/player/command>", true, false, false);
                        break;
                }
            }
        }

        public string centerText(string text)
        {
            return text.PadLeft((40 + (text.Length / 2)), ' '); 
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

        #region Alias stuff

        public string aliasText(string preText)
        {
            return aliasText(preText, "");
        }

        public string aliasText(string preText, string stack)
        {
            // preText = the original command text, stack is the optional text supplied to the alias command
            // for each %1 in preText, take a word off the front of stack and replace
            // for each %0, dump in the remaining stack
            // Randoms are presented between curly braces, seperated by a pipe - eg {1|2|3|4}

            // First, see if there are any %1's in there
            while (preText.IndexOf("%1") > -1)
            {
                if (stack.Length == 0)
                {
                    // If there's no stack, there's nothing to replace it with!
                    preText = preText.Replace("%1", "");
                }
                else
                {
                    string[] split = stack.Split(new char[] { ' ' }, 2);
                    preText = preText.Substring(0, preText.IndexOf("%1")) + split[0] + preText.Substring(preText.IndexOf("%1") + 2);
                    if (split.Length == 1)
                        stack = "";
                    else
                        stack = split[1];
                }
            }
            // Next, see if there are any %0's
            preText = preText.Replace("%0", stack);

            // Now for the ticksy part ... sorting out if there are any randoms
            while (preText.IndexOf("{") > -1 && preText.IndexOf("}") > preText.IndexOf("{"))
            {
                string toReplace = preText.Substring(preText.IndexOf("{"), (preText.IndexOf("}") - preText.IndexOf("{"))+1);
                if (toReplace.IndexOf("|") == -1)
                    preText = preText.Replace(toReplace, toReplace.Substring(1, toReplace.Length - 2));
                else
                {
                    string[] options = toReplace.Substring(1, toReplace.Length - 2).Split(new char[] { '|' });
                    preText = preText.Replace(toReplace, options[new Random().Next(options.Length)]);
                }

            }
            return preText;
        }

        public void cmdAlias(string message)
        {
            if (message == "")
            {
                // Listing aliases
                string output = headerLine("Aliases") + "\r\n";
                if (myPlayer.AliasList.Count == 0)
                    output += "No aliases defined\r\n";
                else
                {
                    string alist = "";
                    foreach (Player.alias a in myPlayer.AliasList)
                    {
                        alist += "{bold}{blue}" + a.aliasName + "{reset} : " + a.aliasCommand + "\r\n";
                    }
                    output += alist;
                }
                output += footerLine();
                if (myPlayer.LogonScript != "")
                {
                    output += "\r\n{bold}{blue}Logon Script {reset}: " + myPlayer.LogonScript + "\r\n" + footerLine();
                }
                sendToUser(output, true, false, false);
            }
            else 
            {
                string aliasName = "";
                string aliasText = "";
                if (message.IndexOf(" ") == -1)
                    aliasName = message;
                else
                {
                    string[] split = message.Split(new char[] { ' ' }, 2);
                    aliasName = split[0];
                    aliasText = split[1];
                }

                if (myPlayer.IsAlias(aliasName))
                {
                    if (aliasText == "")
                    {
                        sendToUser("Alias \"" + aliasName + "\" deleted", true, false, false);
                        myPlayer.DeleteAlias(aliasName);
                    }
                    else
                    {
                        sendToUser("Alias \"" + aliasName + "\" updated", true, false, false);
                        myPlayer.UpdateAlias(aliasName, aliasText);
                    }
                }
                else
                {
                    if (aliasText == "")
                        sendToUser("Alias \"" + aliasName + "\" not found to delete", true, false, false);
                    else
                    {
                        sendToUser("Alias \"" + aliasName + "\" defined", true, false, false);
                        myPlayer.AddAlias(aliasName, aliasText);
                    }
                }
                myPlayer.SavePlayer();
            }
        }

        public void doLAlias()
        {
            if (myPlayer.LogonScript != "")
            {
                string[] split = myPlayer.LogonScript.Split(new char[] { ';' });
                foreach (string s in split)
                {
                    string cmd = aliasText(s);
                    ProcessLine(cmd, true);
                }
            }
        }

        public bool doAlias(string message)
        {
            bool ret = false;
            if (myPlayer.IsAlias(message))
            {
                ret = true;
                string[] split = myPlayer.GetAliasCommand(message).Split(new char[] { ';' });
                foreach (string s in split)
                {
                    ProcessLine(s, true);
                }
                doPrompt();
            }
            return ret;
        }

        #endregion

        #region Friends stuff

        public void cmdFriend(string message)
        {
            if (message == "")
            {
                string output = "";
                if (myPlayer.FriendsArray.Count == 0)
                {
                    output = "You have no friends";
                }
                else
                {
                    int tabcount = 1;
                    foreach (string friend in myPlayer.FriendsArray)
                    {
                        Player temp = Player.LoadPlayer(friend,0);
                        output += ((tabcount++ +1)% 4 == 0 ? "\r\n" : "") + temp.UserName.PadRight(20, ' ').Replace(temp.UserName, temp.ColourUserName);
                    }
                }
                sendToUser("{bold}{cyan}---[{red}Friends{cyan}]".PadRight(103, '-') + "{reset}\r\n" + output + "\r\n{bold}{cyan}".PadRight(94, '-'), true, false, false);
            }
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("No such player \"" + message + "\"", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target.Length == 1 && (target[0].ToLower() == myPlayer.UserName.ToLower()))
                    sendToUser("You can't add yourself to your own friends list!", true, false, false);
                else
                {
                    if (!myPlayer.isFriend(target[0]))
                    {
                        // Not already in friends list
                        if (myPlayer.AddFriend(target[0]))
                        {
                            sendToUser(target[0] + " added to your friends list", true, false, false);
                            if (isOnline(target[0]))
                                sendToUser(myPlayer.UserName + " has made you " + getGender("poss") + " friend", target[0], true, false, true, false);
                            myPlayer.SavePlayer();
                        }
                        else
                            sendToUser("Sorry, your list is full", true, false, false);
                    }
                    else
                    {
                        myPlayer.RemoveFriend(target[0]);
                        sendToUser(target[0] + " removed from your friends list", true, false, false);
                        myPlayer.SavePlayer();
                    }
                }
            }
        }

        #endregion

        #region Room Stuff

        public void cmdWhere(string message)
        {
            if (message != "")
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            sendToUser(c.myPlayer.ColourUserName + " is " + (c.myPlayer.Hidden && !c.myPlayer.CanFindMe(myPlayer.UserName) ? "hiding" : (c.myPlayer.Hidden && c.myPlayer.CanFindMe(myPlayer.UserName) ? "hiding " : "" ) + "in " + getRoomFullName(c.myPlayer.UserRoom)), true, false, false);
                        }
                    }
                }
            }
            else
            {
                string output = "";
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null)
                    {
                        if (c.myPlayer.UserName == myPlayer.UserName)
                            output += "You are " + (myPlayer.Hidden || myPlayer.Invisible ? "hiding " : "") + "in " + getRoomFullName(myPlayer.UserRoom) + "\r\n";
                        else if (!c.myPlayer.Invisible && (!c.myPlayer.Hidden || myPlayer.PlayerRank >= (int)Player.Rank.Admin) || c.myPlayer.CanFindMe(myPlayer.UserName))
                            output += c.myPlayer.ColourUserName + " is in " + getRoomFullName(c.myPlayer.UserRoom) + (c.myPlayer.Hidden ? " (hidden)" : "") + "\r\n";
                        else if (!c.myPlayer.Invisible)
                            output += c.myPlayer.ColourUserName + " is hiding\r\n";
                        else
                            output += c.myPlayer.ColourUserName + " doesn't seem to be online right now\r\n";
                    }
                }
                sendToUser("{bold}{cyan}---[{red}Where{cyan}]".PadRight(103, '-') + "{reset}\r\n" + (output == "" ? "Strange, there's no one here ... not even you!" : output + "{bold}{cyan}" + "".PadRight(80, '-') + "{reset}"), true, false, false);
            }
        }

        public void cmdExits(string message)
        {
            Room room = getRoom(myPlayer.UserRoom);
            if (room == null)
                sendToUser("Strange - you don't seem to be anywhere!", true, false, false);
            else
            {
                string output = "";
                foreach (string s in room.exits)
                {
                    if (s != "")
                    {
                        Room exit = Room.LoadRoom(s);
                        output += exit.shortName + " -> " + getRoomFullName(s) + "\r\n";
                    }
                }
                if (output == "")
                    sendToUser("There are no exits from this room", true, false, false);
                else
                    sendToUser("The following exits are availale:\r\n" + output, true, false, false);
            }
        }

        public void cmdHidePlayer(string message)
        {
            sendToUser((myPlayer.Hidden) ? "You stop hiding" : "You are now hiding", true, false, false);
            myPlayer.Hidden = !myPlayer.Hidden;
            myPlayer.SavePlayer();
        }

        public void cmdGo(string message)
        {
            if (message == "")
                sendToUser("Syntax: Go <exit name>", true, false, false);
            else
            {
                Room currentRoom = getRoom(myPlayer.UserRoom);
                if (currentRoom != null)
                {
                    bool found = false;
                    foreach(string s in currentRoom.exits)
                    {
                        Room exit = Room.LoadRoom(s);
                        if (exit.fullName != null)
                        {
                            if (exit.shortName.ToLower() == message.ToLower() || exit.shortName.ToLower().StartsWith(message.ToLower()))
                            {
                                found = true;
                                movePlayer(s);
                            }
                        }
                    }
                    if (!found)
                    {
                        foreach (Room r in roomList)
                        {
                            if (r.systemName.ToLower() == message.ToLower())
                            {
                                found = true;
                                Player owner = null;
                                if (!r.systemRoom)
                                {
                                    owner = Player.LoadPlayer(r.roomOwner, 0);
                                }
                                if (((r.locks.FullLock || (r.locks.AdminLock && myPlayer.PlayerRank < (int)Player.Rank.Admin) || (r.locks.StaffLock && myPlayer.PlayerRank < (int)Player.Rank.Staff) || (r.locks.GuideLock && myPlayer.PlayerRank < (int)Player.Rank.Guide) || (!r.systemRoom && r.locks.FriendLock && owner != null && !owner.isFriend(myPlayer.UserName))) && myPlayer.PlayerRank < (int)Player.Rank.HCAdmin) && !owner.HasKey(myPlayer.UserName))
                                {
                                    sendToUser("You try the door, but find it locked shut", true, false, false);
                                }
                                else
                                {
                                    movePlayer(r.systemName);
                                }
                            }
                        }
                        if (!found)
                        {
                            sendToUser("No such room", true, false, false);
                        }
                    }
                }
                else
                {
                    sendToUser("Strange. You don't seem to be in a room at all ...", true, false, false);
                }
            }
        }

        public void cmdHome(string message)
        {
            // First check to see if they have a home room, and if not then create it!
            string roomName = myPlayer.UserName.ToLower() + ".home";
            Room check = Room.LoadRoom(roomName);
            if (check == null || check.fullName == null)
            {
                check = new Room("home", myPlayer.UserName, false);
                check.fullName = myPlayer.UserName + "'" + (myPlayer.UserName.EndsWith("s") ? "" : "s") + " little home";
                check.SaveRoom();
                roomList = loadRooms();
            }
            movePlayer(check.systemName);
        }

        public void cmdJoin(string message)
        {
            if (message == "")
                sendToUser("Syntax: join <player name>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null)
                        {
                            if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                            {
                                if (c.myPlayer.Hidden && !c.myPlayer.CanFindMe(myPlayer.UserName))
                                    sendToUser("Try as you might, you cannot seem to find " + target[0] + " anywhere!", true, false, false);
                                else
                                {
                                    Room targetRoom = Room.LoadRoom(c.myPlayer.UserRoom);
                                    if ((targetRoom.locks.FullLock || (targetRoom.locks.AdminLock && myPlayer.PlayerRank < (int)Player.Rank.Admin) || (targetRoom.locks.StaffLock && myPlayer.PlayerRank < (int)Player.Rank.Staff) || (targetRoom.locks.GuideLock && myPlayer.PlayerRank < (int)Player.Rank.Guide) || (targetRoom.locks.FriendLock && !c.myPlayer.isFriend(myPlayer.UserName))) && !c.myPlayer.HasKey(myPlayer.UserName))
                                    {
                                        sendToUser("You try the door, but find it locked shut", true, false, false);
                                    }
                                    else
                                    {
                                        movePlayer(c.myPlayer.UserRoom);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public void cmdMain(string message)
        {
            if (myPlayer.UserRoom == AppSettings.Default.DefaultLoginRoom)
                sendToUser("You are already in the main room!", true, false, false);
            else
                movePlayer(AppSettings.Default.DefaultLoginRoom);
        }

        public void cmdLook(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (currentRoom != null)
            {
                int center = (40 - (int)(currentRoom.fullName.Length / 2))+currentRoom.fullName.Length;
                sendToUser("{bold}{cyan}".PadRight(92, '-') + "{reset}\r\n" + currentRoom.fullName.PadLeft(center, ' ') + "\r\n" + "{bold}{cyan}".PadRight(92, '-') + "{reset}\r\n" + currentRoom.description + "\r\n{bold}{cyan}".PadRight(94, '-') + "{reset}\r\n", true, false, false);
                doWhoRoom();

                List<Room.roomObjects> contents = currentRoom.roomContents;
                string objects = "";
                foreach (Room.roomObjects o in contents)
                {
                    if (o.name != "")
                        objects += o.count.ToString() + " " + o.name + (o.count > 1 ? (o.name.ToLower().EndsWith("s") ? "" : "s") : "") + "\r\n";
                }
                if (objects != "")
                    sendToUser("\r\n\r\nThe following objects are here:\r\n" + objects);
            }
            else
                sendToUser("Strange, you don't seem to be anywhere!", true, false, false);
        }

        public void doWhoRoom()
        {
            string output = "";
            foreach (Connection c in connections)
            {
                if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName != myPlayer.UserName && c.myPlayer.UserRoom == myPlayer.UserRoom)
                    output += ", " + c.myPlayer.ColourUserName;
            }
            if (output == "")
                sendToUser("You are here by yourself\r\n", true, false, false);
            else
                sendToUser("The following people are here:\r\n\r\n" + output.Substring(2) + "\r\n", true, false, false);
        }

        public void movePlayer(string room)
        {
            movePlayer(room, true);
        }

        public void movePlayer(string room, bool doLook)
        {
            movePlayer(room, doLook, true);
        }

        public void movePlayer(string room, bool doLook, bool doEnterMsg)
        {
            Room target = getRoom(room);
            if (target == null)
            {
                sendToUser("Sorry, that room is not valid", true, false, false);
                logError("Room \"" + room + "\" appears in a link from " + myPlayer.UserRoom + " but is not valid", "room");
            }
            else
            {
                Room currentRoom = Room.LoadRoom(myPlayer.UserRoom);
                if (currentRoom != null)
                    sendToRoom(myPlayer.ColourUserName + " " + myPlayer.ExitMsg, "");

                myPlayer.UserRoom = target.systemName;
                if (doEnterMsg)
                    sendToRoom(myPlayer.ColourUserName + " " + (target.enterMessage == "" ? myPlayer.EnterMsg : target.enterMessage), "", false, true);

                myPlayer.SavePlayer();
                if (doLook)
                {
                    sendToUser("\r\n", true, false, false);
                    cmdLook("");
                }
            }
        }

        public void cmdRoomMessage(string message)
        {
            if (message == "")
                sendToUser("Syntax: roommsg DEL/SHOW/<min seconds> <max seconds> <message>", true, false, false);
            else
            {
                Room currentRoom = getRoom(myPlayer.UserRoom);
                if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                {
                    if (message.ToLower() == "del")
                    {
                        currentRoom.remRoomMessage();
                        sendToUser("Room message removed", true, false, false);
                        //currentRoom.SaveRoom();
                        roomList = loadRooms();
                    }
                    else if (message.ToLower() == "show")
                    {
                        if (currentRoom.roomMessage.message == null || currentRoom.roomMessage.message == "")
                            sendToUser("No room message set", true, false, false);
                        else
                            sendToUser("Room message set to \"" + currentRoom.roomMessage.message + "\", firing every " + currentRoom.roomMessage.minTime.ToString() + (currentRoom.roomMessage.minTime == currentRoom.roomMessage.maxTime ? "" : " to " + currentRoom.roomMessage.maxTime.ToString()) + " seconds", true, false, false);
                    }
                    else
                    {
                        string[] split = message.Split(new char[] { ' ' }, 3);
                        if (split.Length < 3)
                        {
                            sendToUser("Syntax: roommsg DEL/SHOW/<min seconds> <max seconds> <message>", true, false, false);
                        }
                        else
                        {
                            int minTime;
                            int maxTime;
                            if (int.TryParse(split[0], out minTime) && int.TryParse(split[1], out maxTime))
                            {
                                if (maxTime < minTime)
                                    sendToUser("Max seconds cannot be less than Min seconds", true, false, false);
                                else
                                {
                                    currentRoom.setRoomMessage(split[2], minTime, maxTime, minTime != maxTime);
                                    //currentRoom.SaveRoom();
                                    roomList = loadRooms();
                                    sendToUser("Room message set to \"" + split[2] + "\", firing every " + minTime.ToString() + (minTime == maxTime ? "" : " to " + maxTime.ToString()) + " seconds", true, false, false);
                                }
                            }
                            else
                            {
                                sendToUser("Sorry, that is not a valid " + (!int.TryParse(split[0], out minTime) ? "min" : "max") + " seconds value", true, false, false);
                            }
                        }
                    }
                }
                else
                {
                    sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
                }
            }
        }

        public void cmdRoomEdit(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                myPlayer.InRoomEditor = true;
                editText = currentRoom.description;
                sendToUser("Entering room editor", true, false, false);
            }
            else
            {
                sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
            }
        }

        public void cmdRoomFullName(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                if (message == "")
                    sendToUser("Syntax: roomfname <full name>", true, false, false);
                else
                {
                    currentRoom.fullName = message;
                    currentRoom.SaveRoom();
                    roomList = loadRooms();
                    sendToUser("Room name set to \"" + currentRoom.fullName + "\"", true, false, false);
                }
            }
            else
            {
                sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
            }
        }

        public void cmdRoomShortName(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            
            if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                if (message == "")
                    sendToUser("Current Room short name: " + currentRoom.shortName, true, false, false);
                else if (message.IndexOf(" ") > -1)
                    sendToUser("Sorry, Room short names cannot contain spaces");
                else
                {
                    // Make sure that we append the username if it's not a system room!

                    roomList = loadRooms();


                    currentRoom.shortName = message;
                    currentRoom.SaveRoom();


                    roomList = loadRooms();
                    sendToUser("Room name set to \"" + currentRoom.systemName+ "\"", true, false, false);
                }
            }
            else
            {
                sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
            }
        }

        public void cmdRoomEnterMsg(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                if (message == "")
                    sendToUser("Current room entermsg: \"" + currentRoom.enterMessage + "\"", true, false, false);
                else if (message.ToLower() == "del")
                {
                    currentRoom.enterMessage = "";
                    currentRoom.SaveRoom();
                    roomList = loadRooms();
                    sendToUser("You have removed the room entermsg", true, false, false);
                }
                else
                {
                    currentRoom.enterMessage = message;
                    currentRoom.SaveRoom();
                    roomList = loadRooms();
                    sendToUser("Room entermsg set to \"" + currentRoom.enterMessage + "\"", true, false, false);
                }
            }
            else
            {
                sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
            }
        }

        public void cmdRoomAdd(string message)
        {
            if (message == "")
                sendToUser("Syntax: roomadd " + (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? "<#>" : "") + "<room system name>", true, false, false);
            else
            {
                bool sysroom = false;
                if (message.StartsWith("#") && myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                    sysroom = true;

                if (!sysroom && roomCount() >= myPlayer.MaxRooms && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                    sendToUser("Sorry, you have reached your maximum number of rooms", true, false, false);
                else
                {
                    Room newRoom = new Room(sysroom ? message.Substring(1).ToLower() : message.ToLower(), myPlayer.UserName, sysroom);
                    newRoom.SaveRoom();
                    roomList = loadRooms();
                    sendToUser("Room \"" + newRoom.shortName + "\" (" + newRoom.systemName + ") created. Remember to link the room somewhere!", true, false, false);
                }
            }
        }

        public void cmdRoomDel(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (currentRoom.systemRoom && currentRoom.systemName.ToLower() == AppSettings.Default.DefaultLoginRoom.ToLower() && myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                sendToUser("You cannot delete the default login room!", true, false, false);
            }
            else if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                // Remove all links to this room!
                for (int i = 0; i < roomList.Count; i++)
                {
                    Room temp = roomList[i];
                    for (int j = 0; j < temp.exits.Count; j++)
                    {
                        if (temp.exits[j].ToLower() == currentRoom.systemName.ToLower())
                            temp.exits[j] = "";
                    }
                    temp.SaveRoom();
                }
                string path = Path.Combine(Server.userFilePath,("rooms" + Path.DirectorySeparatorChar + currentRoom.systemName.ToLower() + ".xml"));

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

                roomList = loadRooms();
                sendToUser("Room \"" + currentRoom.shortName + "\" deleted. Moving you to main", true, false, false);
                movePlayer(AppSettings.Default.DefaultLoginRoom, false);
            }
            else
            {
                sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
            }
        }

        public void cmdRoomLink(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1 || !(message.ToLower().StartsWith("add") || message.ToLower().StartsWith("del")))
                sendToUser("Syntax: roomlink <add/del> <room system name>", true, false, false);
            else
            {
                Room currentRoom = getRoom(myPlayer.UserRoom);
                string[] split = message.Split(new char[] { ' ' });

                if (currentRoom.roomOwner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                    sendToUser("Sorry, you do not have permission to link this room", true, false, false);
                else if (currentRoom.systemName.ToLower() == split[1])
                    sendToUser("You cannot link a room to itself!", true, false, false);
                else
                {
                    if (split[0].ToLower() == "add")
                    {
                        roomList = loadRooms();
                        bool found = false;
                        foreach (Room r in roomList)
                        {
                            if (r.systemName.ToLower() == split[1].ToLower())
                            {
                                found = true;
                                if (r.roomOwner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                                    sendToUser("Sorry, you do not have permission to link to that room", true, false, false);
                                else
                                {
                                    bool alreadyLinked = false;
                                    foreach (string s in currentRoom.exits)
                                    {
                                        if (s.ToLower() == r.systemName.ToLower())
                                            alreadyLinked = true;
                                    }
                                    if (alreadyLinked)
                                        sendToUser("That room is already linked to this one", true, false, false);
                                    else
                                    {
                                        currentRoom.exits.Add(r.systemName);
                                        currentRoom.SaveRoom();
                                        sendToUser("You create a link from this room to \"" + r.systemName + "\" (Don't forget to link it back the other way for two way access)", true, false, false);
                                    }
                                }
                            }
                        }
                        if (!found)
                        {
                            sendToUser("Room \"" + split[1] + "\" not found", true, false, false);
                        }
                    }
                    else
                    {
                        string remove = "";
                        foreach (string s in currentRoom.exits)
                        {
                            if (s.ToLower() == split[1].ToLower())
                                remove = s;
                        }
                        if (remove == "")
                            sendToUser("Link \"" + split[1] + "\" not found", true, false, false);
                        else
                        {
                            currentRoom.exits.Remove(remove);
                            currentRoom.SaveRoom();
                            roomList = loadRooms();
                            sendToUser("You delete the link from this room to \"" + remove + "\"", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdRoomList(string message)
        {
            string output = "\r\n{red}" + "System Name".PadRight(20) + "Short Name".PadRight(20) + "Full Name".PadRight(20) + "Owner{reset}\r\n";
            if (message == "")
            {
                // List all the rooms
                roomList = loadRooms();
                int roomCount = 0;
                foreach (Room r in roomList)
                {
                    output += (r.systemName.Length >= 20 ? r.systemName.Substring(0,16) + "..." : r.systemName).PadRight(20) 
                        + (r.shortName.Length >= 20 ? r.shortName.Substring(0,16) + "..." : r.shortName).PadRight(20) 
                        + (r.fullName.Length >= 20 ? r.fullName.Substring(0,16) + "..." : r.fullName).PadRight(20) 
                        + (r.roomOwner.Length >= 20 ? r.roomOwner.Substring(0,16) + "..." : r.roomOwner) + "\r\n";
                    roomCount++;
                }
                sendToUser(headerLine("All Rooms") + "\r\nThere " + (roomCount != 1 ? "are " + roomCount.ToString() + " rooms" : "is one room") + ":" + output + footerLine(), true, false, false);
            }
            else
            {
                roomList = loadRooms();
                int roomCount = 0;
                foreach (Room r in roomList)
                {
                    if (r.roomOwner.ToLower().StartsWith(message.ToLower()))
                    {
                        output += (r.systemName.Length >= 20 ? r.systemName.Substring(0, 16) + "..." : r.systemName).PadRight(20)
                        + (r.shortName.Length >= 20 ? r.shortName.Substring(0, 16) + "..." : r.shortName).PadRight(20)
                        + (r.fullName.Length >= 20 ? r.fullName.Substring(0, 16) + "..." : r.fullName).PadRight(20)
                        + (r.roomOwner.Length >= 20 ? r.roomOwner.Substring(0, 16) + "..." : r.roomOwner) + "\r\n";
                        roomCount++;
                    }
                }
                sendToUser(headerLine("Rooms matching \"" + message + "\"") + "\r\nThere " + (roomCount != 1 ? "are " + roomCount.ToString() + " rooms" : "is one room") + ":" + output + footerLine(), true, false, false);
            }
        }

        public void cmdRoomLock(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (message == "")
            {
                if (currentRoom.roomOwner.ToLower() == myPlayer.UserName.ToLower() || myPlayer.PlayerRank > (int)Player.Rank.Staff)
                {
                    string output = headerLine("Current room locks for " + currentRoom.shortName) + "\r\n";
                    output += "Full lock:".PadRight(15) + (currentRoom.locks.FullLock ? "Yes" : "No") + "\r\n";
                    if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                    {
                        output += "Admin lock:".PadRight(15) + (currentRoom.locks.AdminLock ? "Yes" : "No") + "\r\n";
                        output += "Staff lock:".PadRight(15) + (currentRoom.locks.StaffLock ? "Yes" : "No") + "\r\n";
                        output += "Guide lock:".PadRight(15) + (currentRoom.locks.GuideLock ? "Yes" : "No") + "\r\n";
                    }
                    output += "Friends lock:".PadRight(15) + (currentRoom.locks.FriendLock ? "Yes" : "No") + "\r\n";
                    output += footerLine();
                    sendToUser(output, true, false, false);
                }
                else
                    sendToUser("You are not authorised to see the locks on this room", true, false, false);
            }
            else
            {
                if (currentRoom.roomOwner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                    sendToUser("Sorry, you do not have permission to change the locks in this room", true, false, false);
                else
                {
                    switch (message.ToLower())
                    {
                        case "full":
                            currentRoom.locks.FullLock = !currentRoom.locks.FullLock;
                            sendToUser("You " + (currentRoom.locks.FullLock ? "add" : "remove") + " the full room lock", true, false, false);
                            break;
                        case "admin":
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                currentRoom.locks.AdminLock = !currentRoom.locks.AdminLock;
                                sendToUser("You " + (currentRoom.locks.AdminLock ? "add" : "remove") + " the admin only lock", true, false, false);
                            }
                            else
                            {
                                sendToUser("Syntax: roomlock <full/friends/none>", true, false, false);
                            }
                            break;
                        case "staff":
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                currentRoom.locks.StaffLock = !currentRoom.locks.StaffLock;
                                sendToUser("You " + (currentRoom.locks.StaffLock ? "add" : "remove") + " the staff only lock", true, false, false);
                            }
                            else
                            {
                                sendToUser("Syntax: roomlock <full/friends/none>", true, false, false);
                            }
                            break;
                        case "guide":
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                currentRoom.locks.GuideLock = !currentRoom.locks.GuideLock;
                                sendToUser("You " + (currentRoom.locks.GuideLock ? "add" : "remove") + " the guide only lock", true, false, false);
                            }
                            else
                            {
                                sendToUser("Syntax: roomlock <full/friends/none>", true, false, false);
                            }
                            break;
                        case "friend":
                            currentRoom.locks.FriendLock = !currentRoom.locks.FriendLock;
                            sendToUser("You " + (currentRoom.locks.FriendLock ? "add" : "remove") + " the friends only lock", true, false, false);
                            break;
                        case "all":
                            currentRoom.locks.FullLock = true;
                            currentRoom.locks.FriendLock = true;
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                currentRoom.locks.AdminLock = true;
                                currentRoom.locks.StaffLock = true;
                                currentRoom.locks.GuideLock = true;
                            }
                            sendToUser("You add all the locks", true, false, false);
                            break;
                        case "none":
                            currentRoom.locks.FullLock = false;
                            currentRoom.locks.FriendLock = false;
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                currentRoom.locks.AdminLock = false;
                                currentRoom.locks.StaffLock = false;
                                currentRoom.locks.GuideLock = false;
                            }
                            sendToUser("You remove all the locks", true, false, false);
                            break;
                        default:
                            sendToUser("Syntax: roomlock <full/" + (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? "admin/staff/guide/all/" : "") + "friends/none>", true, false, false);
                            break;
                    }
                    currentRoom.SaveRoom();
                    roomList = loadRooms();
                }
            }
        }

        public void cmdBarge(string message)
        {
            if (message == "")
                sendToUser("Syntax: barge <username>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                else if (target[0] == myPlayer.UserName)
                    sendToUser("Trying to barge in on yourself?", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0])
                        {
                            if (c.myPlayer.PlayerRank < myPlayer.PlayerRank)
                            {
                                movePlayer(c.myPlayer.UserRoom, false);
                                sendToUser("You have barged in on " + c.myPlayer.UserName, true, false, false);
                            }
                            else
                                sendToUser("I can't let you do that, Dave ....", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdGrab(string message)
        {
            if (message == "")
                sendToUser("Syntax: grab <username>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                else if (target[0] == myPlayer.UserName)
                    sendToUser("Trying to grab yourself?", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            if (c.myPlayer.CanGrabMe(myPlayer.UserName) || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                if (c.myPlayer.UserRoom.ToLower() == myPlayer.UserRoom.ToLower())
                                    sendToUser("You are already in the same room as " + c.myPlayer.ColourUserName, true, false, false);
                                else
                                {
                                    sendToUser("You grab " + c.myPlayer.ColourUserName + " and drop them next to you", true, false, false);
                                    c.sendToUser("\r\nAn etherial hand reaches down, lifts you up and drops you next to " + myPlayer.ColourUserName, true, true, false);
                                    c.movePlayer(myPlayer.UserRoom, false, false);
                                }
                            }
                            else
                            {
                                sendToUser("You can't seem to get a grasp on " + c.myPlayer.ColourUserName, true, false, false);
                            }
                        }
                    }
                }
            }
        }

        public int roomCount()
        {
            roomList = loadRooms();
            int ret = 0;
            foreach (Room r in roomList)
            {
                if (r.roomOwner.ToLower() == myPlayer.UserName.ToLower())
                    ret++;
            }
            return ret;
        }

        public void cmdTrans(string message)
        {
            string[] split = message.Split(new char[]{' '});
            if (message == "" || split.Length != 2)
                sendToUser("Syntax: trans <player> <room>", true, false, false);
            else
            {
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                else
                {
                    Room targRoom = getRoom(split[1]);
                    if (targRoom != null)
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                            {
                                if (c.myPlayer.UserRoom.ToLower() == targRoom.systemName.ToLower())
                                    sendToUser((c.myPlayer.UserName == myPlayer.UserName ? "You are " : c.myPlayer.UserName + " is ") + "already there!", true, false, false);
                                else
                                {
                                    c.movePlayer(targRoom.systemName);
                                    sendToUser("As you wish ...", true, false, false);
                                }
                                return;
                            }
                        }
                        sendToUser("Strange - something wierd has happened!", true, false, false);
                    }
                    else
                        sendToUser("Room \"" + split[1] + "\" not found", true, false, false);
                }
            }

        }

        #endregion

        #region Object stuff

        public void cmdCreate(string message)
        {
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else if (message == "")
                sendToUser("Syntax: create <object name>", true, false, false);
            else
            {
                playerObjects = loadObjects();
                foreach (objects o in playerObjects)
                {
                    if (o.Name.ToLower() == message.ToLower())
                    {
                        sendToUser("That name already exists", true, false, false);
                        return;
                    }
                }
                objects newObj = new objects();
                newObj.Name = message;
                newObj.Creator = myPlayer.UserName;
                newObj.Owner = myPlayer.UserName;
                newObj.Gender = (gender)myPlayer.Gender;

                playerObjects.Add(newObj);
                saveObjects();
                sendToUser("Object \"" + newObj.Name + "\" created");
            }
        }

        public void cmdEdObj(string message)
        {
            string[] split = message.Split(new char[] { ' ' }, 3);
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else if (message == "" || split.Length < 2)
                sendToUser("Syntax: edobj <object name> <command part> <action>", true, false, false);
            else
            {
                playerObjects = loadObjects();
                for (int i = playerObjects.Count - 1; i >= 0; i--)
                {
                    if (playerObjects[i].Name.ToLower() == split[0].ToLower())
                    {
                        objects temp = playerObjects[i];
                        switch (split[1].ToLower())
                        {
                            case "drop":
                                temp.Actions.Drop = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Drop == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "eat":
                                temp.Actions.Eat = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Eat == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "drink":
                                temp.Actions.Drink = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Drink == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "examine":
                                temp.Actions.Examine = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Examine == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "get":
                                temp.Actions.Get = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Get == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "give":
                                temp.Actions.Give = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Give == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "pick":
                                temp.Actions.Pick = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Pick == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "play":
                                temp.Actions.Play = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Play == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "poke":
                                temp.Actions.Poke = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Poke == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "pull":
                                temp.Actions.Pull = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Pull == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "push":
                                temp.Actions.Push = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Push == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "shake":
                                temp.Actions.Shake = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Shake == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "take":
                                temp.Actions.Take = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Take == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "throw":
                                temp.Actions.Throw = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Throw == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "use":
                                temp.Actions.Use = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Use == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "wield":
                                temp.Actions.Wield = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Wield == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "desc":
                                temp.Description = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Description == "" ? "remove" : "set") + " the description for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "weight":
                                int output;
                                if (split.Length == 3 && int.TryParse(split[2], out output))
                                {
                                    if (output >= 0)
                                    {
                                        temp.Weight = (split.Length < 3 ? 0 : output);
                                        sendToUser("You " + (temp.Weight == 0 ? "remove" : "set") + " the weight for object \"" + temp.Name + "\"", true, false, false);
                                    }
                                    else
                                        sendToUser("Negative weights are not allowed", true, false, false);
                                }
                                else
                                {
                                    sendToUser("Weight must be a positive integer", true, false, false);
                                }
                                break;
                            case "punique":
                                temp.Unique.ToPlayer = !temp.Unique.ToPlayer;
                                sendToUser("You " + (temp.Unique.ToPlayer ? "set" : "remove") + " the Unique for player flag for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "sunique":
                                temp.Unique.ToSystem = !temp.Unique.ToPlayer;
                                sendToUser("You " + (temp.Unique.ToSystem ? "set" : "remove") + " the Unique for whole system flag for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "candrop":
                                temp.CanDropOnFloor = !temp.CanDropOnFloor;
                                sendToUser("You " + (temp.CanDropOnFloor ? "set" : "remove") + " the \"Can drop on floor\" flag for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            default:
                                sendToUser("Syntax: edobj <object name> <command part> <action>", true, false, false);
                                break;
                        }
                        playerObjects[i] = temp;
                        saveObjects();
                        return;
                    }
                }
                sendToUser("Object \"" + split[0] + "\" not found", true, false, false);
            }
        }

        public void cmdDelObj(string message)
        {
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else if (message == "")
                sendToUser("Syntax: delobj <object name>", true, false, false);
            else
            {
                playerObjects = loadObjects();
                for (int i = playerObjects.Count-1; i >= 0; i--)
                {
                    if (playerObjects[i].Name.ToLower() == message.ToLower())
                    {
                        if (playerObjects[i].Creator.ToLower() == myPlayer.UserName.ToLower())
                        {
                            objects temp = playerObjects[i];
                            temp.Deleted = true;
                            playerObjects[i] = temp;
                            sendToUser("Object \"" + message + "\" deleted", true, false, false);
                            saveObjects();
                        }
                        else
                        {
                            sendToUser("You are not the owner of that object!", true, false, false);
                        }
                        return;
                    }
                }
                sendToUser("Object \"" + message + "\" not found", true, false, false);
            }
        }

        public void cmdExObj(string message)
        {
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else if (message == "")
                sendToUser("Syntax: exobj <object name>", true, false, false);
            else
            {
                playerObjects = loadObjects();

                objects s = new objects();
                s.Name = "Test";
                s.Owner = "Alchamist";
                s.Weight = 100;
                s.Unique.ToPlayer = true;
                s.Actions.Push = "Wibble";
                s.Actions.Take = "Plink";
                playerObjects.Add(s);

                foreach (objects o in playerObjects)
                {
                    if (o.Name.ToLower() == message.ToLower() && !o.Deleted)
                    {
                        string output = headerLine("Object: " + o.Name) + "\r\n";
                        output += "^BName: ^N" + o.Name.PadRight(14) + "^BOwner: ^N" + o.Owner.PadRight(15) + "^BWeight: ^N" + o.Weight.ToString().PadRight(5);
                        output += "\r\n^BUnique to Player: ^N" + (o.Unique.ToPlayer ? "Yes" : "No").PadRight(20) + "^BUnique to System: ^N" + (o.Unique.ToSystem ? "Yes" : "No");
                        output += "\r\n^BDescription: ^N" + o.Description + "\r\n";
                        output += footerLine() + "\r\n";
                        output += "^BActions:^N\r\n\r\n";
                        output += "^PGet: ^N".PadLeft(15) + o.Actions.Get + "\r\n";
                        output += "^PDrop: ^N".PadLeft(15) + o.Actions.Drop + "\r\n";
                        output += "^PDrink: ^N".PadLeft(15) + o.Actions.Drink + "\r\n";
                        output += "^PEat: ^N".PadLeft(15) + o.Actions.Eat + "\r\n";
                        output += "^PExamine: ^N".PadLeft(15) + o.Actions.Examine + "\r\n";
                        output += "^PGive: ^N".PadLeft(15) + o.Actions.Give + "\r\n";
                        output += "^PPick: ^N".PadLeft(15) + o.Actions.Pick + "\r\n";
                        output += "^PPlay: ^N".PadLeft(15) + o.Actions.Play + "\r\n";
                        output += "^PPoke: ^N".PadLeft(15) + o.Actions.Poke + "\r\n";
                        output += "^PPull: ^N".PadLeft(15) + o.Actions.Pull + "\r\n";
                        output += "^PPush: ^N".PadLeft(15) + o.Actions.Push + "\r\n";
                        output += "^PShake: ^N".PadLeft(15) + o.Actions.Shake + "\r\n";
                        output += "^PTake: ^N".PadLeft(15) + o.Actions.Take + "\r\n";
                        output += "^PThrow: ^N".PadLeft(15) + o.Actions.Throw + "\r\n";
                        output += "^PUse: ^N".PadLeft(15) + o.Actions.Use + "\r\n";
                        output += "^PWield: ^N".PadLeft(15) + o.Actions.Wield + "\r\n";
                        output += footerLine();
                        sendToUser(output, true, false, false);
                        return;
                    }
                }
                sendToUser("Object \"" + message + "\" not found", true, false, false);
            }
        }

        public void listObjects(string message)
        {
            playerObjects = loadObjects();
            
            string output = "";
            int place = 1;
            foreach (objects o in playerObjects)
            {
                if (!o.Deleted)
                {
                    if (message == "" || o.Name.StartsWith(message))
                        output += "^B( " + place++.ToString().PadLeft(playerObjects.Count.ToString().Length,'0') + " )^N " + (o.Unique.ToSystem ? "^Y*^N" : (o.Unique.ToPlayer ? "^R*^N" : " ")) + o.Name.PadRight(14) + "^BOwner: ^N" + o.Owner.PadRight(15) + "^BWeight: ^N" + o.Weight.ToString().PadRight(5) + "\r\n^BDescription: ^N" + o.Description + "\r\n";
                }
            }
            sendToUser(headerLine("Objects: " + (message == "" ? "All" : message)) + "\r\n" + (output == "" ? "No objects found" : output) + "\r\n" + footerLine(), true, false, false);
        }

        private int doObjectCode(string objectName, string action)
        {
            // Return codes - 0 = found and ok, 1 = not in inventory, 2 = object deleted, 3 = no code to run
            playerObjects = loadObjects();
            if (myPlayer.InInventory(objectName)==0)
                return 1;
            foreach (objects o in playerObjects)
            {
                if (o.Name.ToLower() == objectName.ToLower())
                {
                    if (o.Deleted)
                        return 2;
                    else
                    {
                        #region Select Action ...

                        string code = "";
                        switch (action.ToLower())
                        {
                            case "drop":
                                code = o.Actions.Drop;
                                break;
                            case "eat":
                                code = o.Actions.Eat;
                                break;
                            case "drink":
                                code = o.Actions.Drink;
                                break;
                            case "examine":
                                code = o.Actions.Examine;
                                break;
                            case "get":
                                code = o.Actions.Get;
                                break;
                            case "give":
                                code = o.Actions.Give;
                                break;
                            case "pick":
                                code = o.Actions.Pick;
                                break;
                            case "play":
                                code = o.Actions.Play;
                                break;
                            case "poke":
                                code = o.Actions.Poke;
                                break;
                            case "pull":
                                code = o.Actions.Pull;
                                break;
                            case "push":
                                code = o.Actions.Push;
                                break;
                            case "shake":
                                code = o.Actions.Shake;
                                break;
                            case "take":
                                code = o.Actions.Take;
                                break;
                            case "throw":
                                code = o.Actions.Throw;
                                break;
                            case "use":
                                code = o.Actions.Use;
                                break;
                            case "wield":
                                code = o.Actions.Wield;
                                break;
                        }

                        #endregion

                        if (code != "")
                        {
                            string[] split = code.Split(new char[] { ';' });

                            #region Loop one - do the if statements

                            for (int i = 0; i < split.Length; i++)
                            {
                                string tCode = "";
                                switch (split[i].Substring(0, 4))
                                {
                                    case "%ipn": // If playerRank = newbie
                                        tCode = (myPlayer.PlayerRank == (int)Player.Rank.Newbie ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ipr": // If playerRank = resident
                                        tCode = (myPlayer.PlayerRank == (int)Player.Rank.Member ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ipg": // If playerRank = guide
                                        tCode = (myPlayer.PlayerRank == (int)Player.Rank.Guide ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ips": // If playerRank = staff
                                        tCode = (myPlayer.PlayerRank == (int)Player.Rank.Staff ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ipa": // If playerRank = admin
                                        tCode = (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ign": // If playerRank > newbie
                                        tCode = (myPlayer.PlayerRank > (int)Player.Rank.Newbie ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%igr": // If playerRank > resident
                                        tCode = (myPlayer.PlayerRank > (int)Player.Rank.Member ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%igg": // If playerRank > guide
                                        tCode = (myPlayer.PlayerRank > (int)Player.Rank.Guide ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%igs": // If playerRank > staff
                                        tCode = (myPlayer.PlayerRank > (int)Player.Rank.Staff ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ilg": // If playerRank < guide
                                        tCode = (myPlayer.PlayerRank < (int)Player.Rank.Guide ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ils": // If playerRank < staff
                                        tCode = (myPlayer.PlayerRank < (int)Player.Rank.Staff ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ila": // If playerRank < admin
                                        tCode = (myPlayer.PlayerRank < (int)Player.Rank.Admin ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    default:
                                        tCode = split[i];
                                        break;
                                }
                                split[i] = tCode.Trim();
                            }

                            #endregion
                            #region Loop two - place in names etc

                            for (int i = 0; i < split.Length; i++)
                            {
                                split[i] = split[i].Replace("%pnm", myPlayer.UserName); // Player name
                                split[i] = split[i].Replace("%onm", o.Owner); // Owner name
                                split[i] = split[i].Replace("%cnm", o.Creator); // Creator name
                                split[i] = split[i].Replace("%obn", o.Name); // Object name

                                split[i] = split[i].Replace("%psp", (myPlayer.Gender == 0 ? "it" : (myPlayer.Gender == 1 ? "he" : "she"))); // Player subject pronoun (he/she)
                                split[i] = split[i].Replace("%pop", (myPlayer.Gender == 0 ? "it" : (myPlayer.Gender == 1 ? "him" : "her"))); // Player object pronoun (him/her)
                                split[i] = split[i].Replace("%pap", (myPlayer.Gender == 0 ? "its" : (myPlayer.Gender == 1 ? "his" : "her"))); // Player attributive pronoun (his/her)
                                split[i] = split[i].Replace("%ppn", (myPlayer.Gender == 0 ? "its" : (myPlayer.Gender == 1 ? "his" : "hers"))); // Player possesove pronoun (his/hers)
                            }

                            #endregion
                            #region Loop three - do the code!

                            for (int i = 0; i < split.Length; i++)
                            {
                                if (split[i] != "")
                                {
                                    switch (split[i].Substring(0, 4))
                                    {
                                        case "%stp": // Send to player
                                            sendToUser(split[i].Replace(split[i].Substring(0, 4), "").Trim());
                                            break;
                                        case "%str": // Send to room
                                            sendToRoom(split[i].Replace(split[i].Substring(0, 4), "").Trim(),"");
                                            break;
                                        case "%sta": // Send to all
                                            if (o.Rank >= Player.Rank.Admin)
                                                sendToAll(split[i].Replace(split[i].Substring(0, 4), "").Trim());
                                            break;
                                        case "%trn": // Transport player
                                            if (o.Rank >= Player.Rank.Admin)
                                                movePlayer(split[i].Replace(split[i].Substring(0, 4), "").Trim());
                                            break;
                                        case "%wib": // Wibble player
                                            if (o.Rank >= Player.Rank.Admin)
                                                myPlayer.Wibbled = true;
                                            break;
                                        case "%jal": // Jail player
                                            if (o.Rank >= Player.Rank.Admin)
                                                movePlayer("jail");
                                            break;
                                        case "%bmp": // Bump player
                                            if (o.Rank >= Player.Rank.Admin)
                                                socket.Close();
                                            break;
                                        case "%wld": // Wield
                                            myPlayer.WieldInventory(o.Name);
                                            break;
                                        case "%drp": // Drop item on floor
                                            if (o.CanDropOnFloor)
                                                dropOnFloor(o.Name);
                                            break;

                                    }
                                }
                            }
                            #endregion
                        }
                        else
                        {
                            return 3;
                        }
                    }
                }
            }
            return 0;
        }

        public void dropOnFloor(string itemName)
        {
            myPlayer.RemoveFromInventory(itemName);
            foreach (Room r in roomList)
            {
                if (r.systemName.ToLower() == myPlayer.UserRoom.ToLower())
                {
                    r.addObject(itemName);
                    return;
                }
            }
        }

        public void getFromFloor(string itemName)
        {
            myPlayer.RemoveFromInventory(itemName);
            foreach (Room r in roomList)
            {
                if (r.systemName.ToLower() == myPlayer.UserRoom.ToLower())
                {
                    r.removeObject(itemName);
                    r.SaveRoom();
                    return;
                }
            }
        }

        public void cmdInventory(string message)
        {
            string output = "";
            int totalWeight = 0;
            List<Player.inventory> inventory = myPlayer.Inventory;
            foreach (Player.inventory i in inventory)
            {
                objects inv = getObject(i.name);
                if (inv.Name != null && !inv.Deleted)
                {
                    output += "^p[" + i.count.ToString().PadLeft(3, '0') + "]^N " + inv.Name + " ^a(" + inv.Weight.ToString() + " units each)^N\r\n";
                    totalWeight += (i.count * inv.Weight);
                }
            }
            if (output == "")
                output = "You have no items\r\nYou can carry a total of " + myPlayer.MaxWeight.ToString() + " units of weight";
            else
                output = "^BQty  Item^N\r\n" + output + "\r\nYou are carrying a total of " + totalWeight.ToString() + " units of weight, and can carry " + (myPlayer.MaxWeight - totalWeight).ToString() + " more" ;

            sendToUser(headerLine("Inventory") + "\r\n" + output + "\r\n" + footerLine());
        }

        public void cmdEdWeight(string message)
        {
            string[] split = message.Split(new char[] { ' ' });
            int weightChange = 0;
            if (message == "" || split.Length != 2 || !int.TryParse(split[1], out weightChange))
                sendToUser("Syntax: edweight <player> <+/- units>", true, false, false);
            else
            {
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            c.myPlayer.MaxWeight += weightChange;
                            sendToUser("You change " + c.myPlayer.UserName + "'s Max weight to " + c.myPlayer.MaxWeight + " units", true, false, false);
                            c.sendToUser(myPlayer.ColourUserName + " has changed the maximum weight you can carry to " + c.myPlayer.MaxWeight + " units", true, false, false);
                            c.myPlayer.SavePlayer();
                            return;
                        }
                    }
                    sendToUser("Strange - something seems to have gone wrong ...", true, false, false);
                }
            }
        }

        private objects getObject(string objectName)
        {
            playerObjects = loadObjects();
            objects ret = new objects();
            foreach (objects o in playerObjects)
            {
                if (o.Name.ToLower() == objectName.ToLower())
                    ret = o;
            }
            return ret;
        }

        private int getInventoryWeight()
        {
            int totalWeight = 0;
            List<Player.inventory> inventory = myPlayer.Inventory;
            foreach (Player.inventory i in inventory)
            {
                objects inv = getObject(i.name);
                if (inv.Name != null && !inv.Deleted)
                {
                    totalWeight += (i.count & inv.Weight);
                }
            }
            return totalWeight;
        }

        public void cmdGet(string message)
        {
            if (message == "")
                sendToUser("Syntax: get <object name>", true, false, false);
            else
            {
                objects get = getObject(message);
                if (get.Name == null || get.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else
                {
                    if (getInventoryWeight() + get.Weight > myPlayer.MaxWeight)
                        sendToUser("Sorry, that is too heavy for you", true, false, false);
                    else if (myPlayer.InInventory(get.Name) > 0 && get.Unique.ToPlayer)
                        sendToUser("Sorry, you can only have one of those", true, false, false);
                    else
                    {
                        bool add = myPlayer.InInventory(get.Name) > 0;
                        myPlayer.AddToInventory(get.Name);

                        if (get.Actions.Get == null || get.Actions.Get == "")
                            sendToUser("You take " + (add ? "another " : (isVowel(get.Name.Substring(0, 1)) ? "an " : "a ")) + get.Name, true, false, false);
                        else
                        {
                            int status = doObjectCode(get.Name, "get");
                            if (status == 1 || status == 2)
                                sendToUser("Sorry - something seems to have gone wrong!", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdTake(string message)
        {
            if (message == "")
                sendToUser("Syntax: take <object name>", true, false, false);
            else
            {
                objects get = getObject(message);
                Room currentRoom = getRoom(myPlayer.UserRoom);

                if (get.Name == null || get.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (currentRoom.isObjectInRoom(get.Name) == 0)
                    sendToUser("You cannot see " + (isVowel(get.Name.Substring(0, 1)) ? "an " : "a ") + get.Name + " here");
                else
                {
                    if (getInventoryWeight() + get.Weight > myPlayer.MaxWeight)
                        sendToUser("Sorry, that is too heavy for you", true, false, false);
                    else if (myPlayer.InInventory(get.Name) > 0 && get.Unique.ToPlayer)
                        sendToUser("Sorry, you can only have one of those", true, false, false);
                    else
                    {
                        bool add = myPlayer.InInventory(get.Name) > 0;
                        getFromFloor(get.Name);

                        myPlayer.AddToInventory(get.Name);
                        

                        if (get.Actions.Get == null || get.Actions.Get == "")
                            sendToUser("You take " + (add ? "another " : (isVowel(get.Name.Substring(0, 1)) ? "an " : "a ")) + get.Name, true, false, false);
                        else
                        {
                            int status = doObjectCode(get.Name, "get");
                            if (status == 1 || status == 2)
                                sendToUser("Sorry - something seems to have gone wrong!", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdDrop(string message)
        {
            if (message == "")
                sendToUser("Syntax: drop <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Drop == null || target.Actions.Drop == "")
                    {
                        sendToUser("You drop " + (moreThanOne ? "one of your " : "your ") + target.Name + (target.Name.ToLower().EndsWith("s") ? "" : (moreThanOne ? "s" : "")), true, false, false);
                        //
                        if (target.CanDropOnFloor)
                            dropOnFloor(target.Name);
                        else
                            myPlayer.RemoveFromInventory(target.Name);
                    }
                    else
                    {
                        if (target.Rank < Player.Rank.Admin)
                            myPlayer.RemoveFromInventory(target.Name);
                        doObjectCode(target.Name, "drop");
                    }
                }
            }
        }

        public void cmdClearRoom(string message)
        {
            foreach (Room r in roomList)
            {
                if (r.systemName.ToLower() == myPlayer.UserRoom.ToLower())
                {
                    r.removeAllObjects();
                    sendToUser("You remove all objects from the floor of the room", true, false, false);
                    logToFile(myPlayer.UserName + " clears up the objects in " + r.fullName, "admin");
                    return;
                }
            }
        }

        public void cmdPlay(string message)
        {
            if (message == "")
                sendToUser("Syntax: play <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Play == null || target.Actions.Play == "")
                    {
                        sendToUser("You play with your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "play");
                    }
                }
            }
        }

        public void cmdEat(string message)
        {
            if (message == "")
                sendToUser("Syntax: eat <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Eat == null || target.Actions.Eat == "")
                    {
                        sendToUser("You find the " + target.Name + " inedible", true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "eat");
                    }
                }
            }
        }

        public void cmdDrink(string message)
        {
            if (message == "")
                sendToUser("Syntax: drink <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Drink == null || target.Actions.Drink == "")
                    {
                        sendToUser("You nearly choke to death trying to drink the " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "drink");
                    }
                }
            }
        }

        public void cmdPick(string message)
        {
            if (message == "")
                sendToUser("Syntax: pick <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Pick == null || target.Actions.Pick == "")
                    {
                        sendToUser("Nothing happens as you pick your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "pick");
                    }
                }
            }
        }

        public void cmdThrow(string message)
        {
            if (message == "")
                sendToUser("Syntax: throw <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Throw == null || target.Actions.Throw == "")
                    {
                        sendToUser("Nothing happens as you throw your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "throw");
                    }
                }
            }
        }

        public void cmdPush(string message)
        {
            if (message == "")
                sendToUser("Syntax: push <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Push == null || target.Actions.Push == "")
                    {
                        sendToUser("Nothing happens as you push your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "push");
                    }
                }
            }
        }

        public void cmdPull(string message)
        {
            if (message == "")
                sendToUser("Syntax: pull <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Pull == null || target.Actions.Pull == "")
                    {
                        sendToUser("Nothing happens as you push your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "pull");
                    }
                }
            }
        }

        public void cmdShake(string message)
        {
            if (message == "")
                sendToUser("Syntax: shake <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Shake == null || target.Actions.Shake == "")
                    {
                        sendToUser("Nothing happens as you shake your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "shake");
                    }
                }
            }
        }

        public void cmdPoke(string message)
        {
            if (message == "")
                sendToUser("Syntax: poke <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Poke == null || target.Actions.Poke == "")
                    {
                        sendToUser("Nothing happens as you poke your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "poke");
                    }
                }
            }
        }

        public void cmdUse(string message)
        {
            if (message == "")
                sendToUser("Syntax: use <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Use == null || target.Actions.Use == "")
                    {
                        sendToUser("You cannot seem to find a way to use your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "use");
                    }
                }
            }
        }

        public void cmdWield(string message)
        {
            if (message == "")
                sendToUser("Syntax: wield <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Wield == null || target.Actions.Wield == "")
                    {
                        sendToUser("You cannot seem to find a way to wield your " + target.Name, true, false, false);
                    }
                    else
                    {
                        myPlayer.WieldInventory(target.Name);
                        if (myPlayer.IsWielded(target.Name))
                        {
                            doObjectCode(target.Name, "wield");
                        }
                        else
                        {
                            sendToUser("You stop wielding your " + target.Name, true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdObjExamine(string message)
        {
            if (message == "")
                sendToUser("Syntax: examine <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Examine == null || target.Actions.Examine == "")
                    {
                        sendToUser("You see nothing special", true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "examine");
                    }
                }
            }
        }

        public bool isVowel(string check)
        {
            check = check.ToLower();
            return (check == "a" || check == "e" || check == "i" || check == "o" || check == "u");
        }

        #endregion

        #region Messaging System

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
                else if (target.Length == 1 &&(target[0].ToLower() == myPlayer.UserName.ToLower()))
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
                                recipients.Add(Player.LoadPlayer(target[0],0));
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
                string path = Path.Combine(Server.userFilePath,@"messages" + Path.DirectorySeparatorChar);
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

        public void saveObjects()
        {
            try
            {
                string path = Path.Combine(Server.userFilePath,@"objects" + Path.DirectorySeparatorChar);
                string fname = "objects.xml";
                string fpath = path + fname;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                XmlSerializer serial = new XmlSerializer(typeof(List<objects>));
                TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                serial.Serialize(textWriter, playerObjects);
                textWriter.Close();
            }
            catch (Exception ex)
            {
                Connection.logError(ex.ToString(), "filesystem");
            }
        }

        public List<objects> loadObjects()
        {
            List<objects> load = new List<objects>();
            string path = Path.Combine(Server.userFilePath,@"objects" + Path.DirectorySeparatorChar);
            string fname = "objects.xml";
            string fpath = path + fname;

            if (Directory.Exists(path) && File.Exists(fpath))
            {
                try
                {
                    XmlSerializer deserial = new XmlSerializer(typeof(List<objects>));
                    TextReader textReader = new StreamReader(@fpath);
                    load = (List<objects>)deserial.Deserialize(textReader);
                    textReader.Close();
                }
                catch (Exception e)
                {
                    Debug.Print(e.ToString());
                }
            }
            return load;
        }

        public List<message> loadMessages()
        {
            List<message> load = new List<message>();
            string path = Path.Combine(Server.userFilePath,@"messages" + Path.DirectorySeparatorChar);
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

        public List<IPAddress> loadIPBans()
        {
            List<string> load = new List<string>();
            string path = Path.Combine(Server.userFilePath,@"banish" + Path.DirectorySeparatorChar);
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
                string path = Path.Combine(Server.userFilePath,@"banish" + Path.DirectorySeparatorChar);
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
            string path = Path.Combine(Server.userFilePath,@"banish" + Path.DirectorySeparatorChar);
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
                string path = Path.Combine(Server.userFilePath,@"banish" + Path.DirectorySeparatorChar);
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


        #endregion

        #region Mail System

        public void cmdMail(string message)
        {
            mail = loadMails();
            // Monster mailing system
            if (message == "")
                sendToUser("Syntax: mail <list/read/send/reply/del>", true, false, false);
            else
            {
                string action = (message.IndexOf(" ") > -1 ? (message.Split(new char[] {' '},2))[0] : message);
                int mailID = 0;
                try
                {
                    mailID = (message.IndexOf(" ") > -1 ? Convert.ToInt32((message.Split(new char[] { ' ' }, 2)[1])) : 0);
                }
                catch(Exception ex)
                {
                    Debug.Print(ex.ToString());
                }

                string body = (message.IndexOf(" ") > -1 ? message.Split(new char[] {' '},2)[1] : "");

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

        public void descriptionEdit(string message)
        {
            if (message.StartsWith("."))
            {
                switch (message)
                {
                    case ".end":
                    case ".":
                        myPlayer.InDescriptionEditor = false;
                        myPlayer.Description = editText;
                        sendToUser("Description set", true, false, false);
                        editText = "";
                        myPlayer.SavePlayer();
                        break;
                    case ".wipe":
                        editText = "";
                        break;
                    case ".view":
                        sendToUser(editText, true, false, false);
                        break;
                    case ".quit":
                        editText = "";
                        myPlayer.InDescriptionEditor = false;
                        sendToUser("Description edit aborted", true, false, false);
                        break;
                    default:
                        sendToUser("Commands available:\r\n.view - show current description content\r\n.wipe - wipe current description content\r\n.quit - exit the editor without saving desctiption\r\n.end - exit the editor and save description", true, false, false);
                        break;
                }
            }
            else
            {
                editText += message + "\r\n";
            }
            doPrompt();
        }

        public void roomEdit(string message)
        {
            if (message.StartsWith("."))
            {
                switch (message)
                {
                    case ".end":
                    case ".":
                        myPlayer.InRoomEditor = false;
                        Room temp = Room.LoadRoom(myPlayer.UserRoom);
                        temp.description = editText;
                        temp.SaveRoom();
                        roomList = loadRooms();
                        sendToUser("Description set", true, false, false);
                        editText = "";
                        break;
                    case ".wipe":
                        editText = "";
                        break;
                    case ".view":
                        sendToUser(editText, true, false, false);
                        break;
                    case ".quit":
                        editText = "";
                        myPlayer.InRoomEditor = false;
                        sendToUser("Description edit aborted", true, false, false);
                        break;
                    default:
                        sendToUser("Commands available:\r\n.view - show current description content\r\n.wipe - wipe current description content\r\n.quit - exit the editor without saving desctiption\r\n.end - exit the editor and save description", true, false, false);
                        break;
                }
            }
            else
            {
                editText += message + "\r\n";
            }
            doPrompt();
        }

        public void saveMails()
        {
            try
            {
                string path = Path.Combine(Server.userFilePath,@"mail" + Path.DirectorySeparatorChar);
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
            string path = Path.Combine(Server.userFilePath,@"mail" + Path.DirectorySeparatorChar);
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

        #endregion

        #region Warnings and Slaps

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
                string[] split = message.Split(new char[] {' '},2);
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

            for(int i = 0; i < messages.Count; i++)
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

        public void doInform(bool login)
        {
            foreach (Connection c in connections)
            {
                if (myPlayer!=null && c.myPlayer.UserName != myPlayer.UserName && c.myPlayer != null)
                {
                    if (c.myPlayer.InformFor(myPlayer.UserName))
                    {
                        string informString = "\r\n^G[Inform] " + myPlayer.ColourUserName + "^G has just logged " + (login ? "in" : "out") + " [" + rankName(myPlayer.PlayerRank) + "^G]";
                        informString += (myPlayer.InformTag == "" ? "" : " [" + myPlayer.InformTag + "^G]");
                        if (c.myPlayer.PlayerRank >= (int)Player.Rank.Staff)
                        {
                            informString += (myPlayer.AutoGit ? " [AutoGit]" : "");
                            informString += (myPlayer.Git ? " [Git]" : "");
                        }
                        c.sendToUser(informString, true, false, false);
                    }
                }
            }
        }

        #endregion

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
                if (c.socket.Connected && c.myPlayer != null)
                {
                    c.Writer.Write(AnsiColour.Colorise("^RSYSTEM IS RESTARTING - BACK IN A JIFFY!\r\n", c.myPlayer.DoColour));
                    c.Writer.Flush();
                    c.socket.Close();
                }
            }
            Server.Restart();
        }

        #endregion

        #region getPlayers

        private List<Player> getPlayers()
        {
            return getPlayers("*", false, false, false, false);
        }

        private List<Player> getPlayers(bool staffOnly, bool builderOnly, bool testerOnly, bool gitsOnly)
        {
            return getPlayers("*", staffOnly, builderOnly, testerOnly, gitsOnly);
        }

        private List<Player> getPlayers(string startsWith)
        {
            return getPlayers(startsWith, false, false, false, false);
        }

        private List<Player> getPlayers(string startsWith, bool staffOnly, bool builderOnly, bool testerOnly, bool gitsOnly)
        {
            List<Player> list = new List<Player>();
            string path = Path.Combine(Server.userFilePath,(@"players" + Path.DirectorySeparatorChar));
            DirectoryInfo di = new DirectoryInfo(path);
            DirectoryInfo[] subs = di.GetDirectories(startsWith);
            //foreach (DirectoryInfo dir in subs)
            //{
                FileInfo[] fi = di.GetFiles();
                foreach (FileInfo file in fi)
                {
                    Player load = Player.LoadPlayer(file.Name.Replace(".xml",""),0);
                    if (load != null && ((staffOnly && load.PlayerRank >= (int)Player.Rank.Guide) || (builderOnly && load.SpecialPrivs.builder) || (testerOnly && load.SpecialPrivs.tester) || (gitsOnly && (load.Git || load.AutoGit))) || (!staffOnly && !builderOnly && !testerOnly && !gitsOnly))
                        list.Add(load);
                }
            //}
            return list;
        }

        private List<Player> getPlayers(int rank, bool singleRankOnly)
        {
            List<Player> list = new List<Player>();
            string path = Path.Combine(Server.userFilePath,(@"players" + Path.DirectorySeparatorChar));
            DirectoryInfo di = new DirectoryInfo(path);
            DirectoryInfo[] subs = di.GetDirectories();
            //foreach (DirectoryInfo dir in subs)
            //{
                FileInfo[] fi = di.GetFiles();
                foreach (FileInfo file in fi)
                {
                    Player load = Player.LoadPlayer(file.Name.Replace(".xml", ""), 0);

                    if (load != null && (load.PlayerRank == rank || (load.PlayerRank > rank && !singleRankOnly)))
                        list.Add(load);
                }
            //}
            return list;
        }

        #endregion

        #region Rooms

        private string getRoomFullName(string room)
        {
            string ret = "";
            foreach (Room r in roomList)
            {
                if (r.systemName == room)
                    ret = r.fullName;
            }
            return ret;
        }

        private Room getRoom(string room)
        {
            roomList = loadRooms();
            Room ret = null;
            foreach (Room r in roomList)
            {
                if (r.systemName.ToLower() == room.ToLower())
                    ret = r;
            }
            return ret;
        }

        #endregion

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
            if (message == "" || message.IndexOf(" ")==-1)
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
            myPlayer.ClubChannelMute = ! myPlayer.ClubChannelMute;
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
                    sendToChannel(targ.Name, myPlayer.UserName + (split[1].Substring(0,1)=="'" ? "" : " ") + split[1], false);
                }
            }
        }

        #endregion


        #endregion

        #region Output stuff

        private string rankName(int rank)
        {
            switch (rank)
            {
                case (int)Player.Rank.HCAdmin:
                    return AppSettings.Default.HCAdminName;
                case (int)Player.Rank.Admin:
                    return AppSettings.Default.AdminName;
                case (int)Player.Rank.Staff:
                    return AppSettings.Default.StaffName;
                case (int)Player.Rank.Guide:
                    return AppSettings.Default.GuideName;
                case (int)Player.Rank.Member:
                    return "Resident";
                default:
                    return "Newbie";
            }
        }

        private string getGender(string type)
        {
            string ret;
            switch (type)
            {
                case "self":
                    ret = (myPlayer.Gender == 0 ? "itself" : (myPlayer.Gender == 1 ? "himself" : "herself"));
                    break;
                case "poss":
                    ret = (myPlayer.Gender == 0 ? "its" : (myPlayer.Gender == 1 ? "his" : "her"));
                    break;
                default:
                    ret = (myPlayer.Gender == 0 ? "it" : (myPlayer.Gender == 1 ? "he" : "she"));
                    break;
            }
            return ret;
        }

        private static bool IsDate(Object obj)
        {
            string strDate = obj.ToString();
            try
            {
                DateTime dt;
                DateTime.TryParse(strDate, out dt);
                if (dt != DateTime.MinValue && dt != DateTime.MaxValue)
                    return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool testEmailRegex(string emailAddress)
        {
            string patternStrict = @"^(([^<>()[\]\\.,;:\s@\""]+"
                  + @"(\.[^<>()[\]\\.,;:\s@\""]+)*)|(\"".+\""))@"
                  + @"((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}"
                  + @"\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+"
                  + @"[a-zA-Z]{2,}))$";

            Regex reStrict = new Regex(patternStrict);

            bool isStrictMatch = reStrict.IsMatch(emailAddress);
            return isStrictMatch;
        }

        private string offlineWho()
        {
            string output = "";
            int count = 0;
            foreach (Connection conn in connections)
            {
                if (conn.myPlayer != null && !conn.myPlayer.Invisible)
                {
                    output += conn.myPlayer.UserName + "\t";
                    if (++count % 4 == 0)
                        output += "\r\n";
                }
            }
            if (count == 1)
                output = "There is one user online\r\n" + output;
            else
                output = "There are " + count.ToString() + " users online\r\n" + output;

            return output;
        }

        private string sayWord(string message, bool toPlayer)
        {
            string end = message.Substring(message.Length - 1);
            string ret = (end == "?" ? "ask" : (end == "!" ? "exclaim" : "say"));
            return toPlayer ? ret : ret + "s";
        }

        private void doPrompt()
        {
            doPrompt(myPlayer.UserName);
        }

        private void doPrompt(string user)
        {
            if (user == myPlayer.UserName)
                sendToUser("\r" + (myPlayer.InEditor ? "> " : myPlayer.Prompt.Replace("%t", DateTime.Now.ToShortTimeString()).Replace("%d", DateTime.Now.ToShortDateString())), false, false, false);
            else
            {
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName == user)
                    {
                        sendToUser("\r" + (c.myPlayer.InEditor ? "> " :  c.myPlayer.Prompt.Replace("%t", DateTime.Now.ToShortTimeString()).Replace("%d", DateTime.Now.ToShortDateString())), c.myPlayer.UserName, false, c.myPlayer.DoColour, false, false);
                    }
                }
            }
        }

        private string[] matchPartial(string name)
        {
            string source = @"players/";
            List<string> pNames = new List<string>();

            if (pNames.Count == 0)
            {
                if (Directory.Exists(source))
                {
                    string[] dirs = Directory.GetDirectories(source);

                    foreach (string subdir in dirs)
                    {
                        string[] fNames = Directory.GetFiles(@subdir);
                        foreach (string n in fNames)
                        {
                            string fn = Path.GetFileNameWithoutExtension(n);
                            if (fn.StartsWith(name, StringComparison.CurrentCultureIgnoreCase))
                            {
                                Player p = Player.LoadPlayer(fn, 0);
                                if (p!= null)
                                    pNames.Add(p.UserName);
                            }
                        }
                    }
                }
            }

            foreach (Connection c in connections)
            {
                if (c.socket.Connected && c.myPlayer != null && c.myPlayer != null && pNames.IndexOf(c.myPlayer.UserName) < 0 && c.myPlayer.UserName.ToLower().StartsWith(name.ToLower()))
                    pNames.Add(c.myPlayer.UserName);
            }

            return pNames.ToArray();
        }

        private bool isOnline(string username)
        {
            bool found = false;
            foreach (Connection c in connections)
            {
                if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == username.ToLower())
                    found = true;
            }
            return found;
        }

        private string wibbleText(string text, bool isEmote)
        {
            if (myPlayer != null && myPlayer.Wibbled)
            {
                if (isEmote)
                {
                    return " shakes a broken rattle";
                }
                else
                {
                    char[] rev = text.ToCharArray();
                    Array.Reverse(rev);
                    return new string(rev);
                }
            }
            else
            {
                return text;
            }
        }

        private string cleanLine(string line)
        {
            string ret = "";
            char[] pLine = line.ToCharArray();
            foreach (char c in pLine)
            {
                switch (c)
                {
                    case '\r':
                        ret += Convert.ToString("\r\n");
                        break;
                    case '\n':
                        break;
                    case '\b':
                        if (ret.Length > 0) ret = ret.Remove(ret.Length - 1, 1);
                        break;
                    default:
                        ret += Convert.ToString(c);
                        break;
                }
            }
            // Strip out echo off and on commands
            ret = ret.Replace("��", "").Replace("��", "");
            return ret;
        }

        private string tellWord(string text)
        {
            return tellWord(text, true);
        }

        private string tellWord(string text, bool toSender)
        {
            string endChar = text.Substring(text.Length-1);
            string ret = "";
            if (toSender)
            {
                switch (endChar)
                {
                    case "?":
                        ret = "ask ";
                        break;
                    case "!":
                        ret = "exclaim to ";
                        break;
                    default:
                        ret = "tell ";
                        break;
                }
            }
            else
            {
                switch (endChar)
                {
                    case "?":
                        ret = "asks you ";
                        break;
                    case "!":
                        ret = "exclaims to you ";
                        break;
                    default:
                        ret = "tells you ";
                        break;
                }
            }
            return ret;
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

        private string formatTime(TimeSpan time)
        {
            string ret = "";
            if (time.Days > 365)
                ret = ((int)(time.Days / 365.25)).ToString() + " years, ";
            if (time.Days > 0)
                ret += ((int)time.Days % 365.25).ToString() + " days, ";
            if (time.Hours > 0 || time.Days > 0)
                ret += time.Hours.ToString() + " hours, ";
            if (time.Minutes > 0 || time.Days > 0 || time.Hours > 0)
                ret += time.Minutes.ToString() + " minutes ";
            if (time.Seconds > 0)
            {
                if (ret != "")
                    ret += "and ";
                ret += time.Seconds.ToString() + " seconds";
            }
            return ret;
        }

        private string formatTimeNoZeros(TimeSpan ts)
        {
            string output = "";
            if (ts.Days > 0)
            {
                if (ts.Days > 365)
                    output += ((int)(ts.Days / 365)).ToString() + " year" + ((((int)(ts.Days / 365)) > 1) ? "s" : "") + ", " + (ts.Days - (((int)(ts.Days / 365))*365)).ToString() + " days, ";
                else
                {
                    output += ts.Days.ToString() + " day" + (ts.Days != 1 ? "s" : "") + ", ";
                }
            }
            if (ts.Hours > 0)
            {
                output += ts.Hours.ToString() + " hour" + (ts.Hours != 1 ? "s" : "") + ", ";
            }
            if (ts.Minutes > 0)
            {
                output += ts.Minutes.ToString() + " minute" + (ts.Minutes != 1 ? "s" : "") + ", ";
            }
            output += ts.Seconds.ToString() + " second" + (ts.Seconds != 1 ? "s" : "");

            return output;
        }
        

        private void sendEchoOff()
        {
            this.socket.Send(echoOff);
        }

        private void sendEchoOn()
        {
            this.socket.Send(echoOn);

        }

        private void flushSocket()
        {
            flushSocket(false);
        }

        private void flushSocket(bool allSockets)
        {
            if (!allSockets)
                Writer.Flush();
            else
            {
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected)
                        c.Writer.Flush();
                }
            }
        }

        private static string headerLine(string title)
        {
            return headerLine(title, "{red}");
        }

        private static string headerLine(string title, string titleColour)
        {
            return ("{bold}{cyan}---[" + titleColour + title + "{bold}{cyan}]").PadRight(104 + titleColour.Length, '-') + "{reset}";
        }

        private static string footerLine()
        {
            return "{bold}{cyan}".PadRight(92, '-') + "{reset}";
        }


        #endregion

    }
}
