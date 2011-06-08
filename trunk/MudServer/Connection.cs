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
        public Socket                      socket;
        public StreamReader         Reader;
        public StreamWriter         Writer;
        public static ArrayList     connections = new ArrayList();
        private int                 myNum = 0;
        public int                  myState = 0; // 0 = new connection, 1 = username supplied, 2 = new player, 3 = password supplied, 4 = active
        public Player               myPlayer;
        private string              connPoint;
        private ArrayList           cmds = new ArrayList();
        public string               lastSent;
        public createUser           newUser = new createUser();
        public DateTime             connectTime = DateTime.Now; // used for tracking how long they have been at the prompt ...

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
            myState = -1;
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

                            if (myPlayer.LastLogon.ToShortDateString() != DateTime.Now.ToShortDateString())
                                Server.playerCountToday++;
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
            Writer.Flush();
        } 

        #endregion

        #region Misc Methods

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

                string fmtMsg = "[all|a-z|";
                foreach (string c in cmdCat)
                {
                    fmtMsg += c + "|";
                }
                if (fmtMsg.Length > 70)
                {
                    int midPoint = ((int)(fmtMsg.Length / 2))-1;
                    int midSplit = fmtMsg.IndexOf("|", midPoint)+1;
                    fmtMsg = fmtMsg.Substring(0, midSplit) + "\r\n             " + fmtMsg.Substring(midSplit);

                }

                fmtMsg = fmtMsg.Remove(fmtMsg.Length - 1) + "]";
                sendToUser("Format: cmd " + fmtMsg, true, false, false);
            }
            else
            {
                List<string> cmdCat = new List<string>();
                foreach (commands c in cmds)
                {
                    if ((c.helpSection == message || message == "all" || (message.Length == 1 && c.cmdText.ToLower().StartsWith(message.ToLower()))) && c.helpSection != "dnl")
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
                    output += "\r\n" + centerText("There are " + cmdCat.Count.ToString() + " commands available for " + message) + "\r\n" + line;
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

        public void cmdFullWho(string message)
        {
            string line = "{bold}{cyan}".PadRight(92, '-') + "{reset}";
            string output = "{bold}{cyan}---[{green}Who{cyan}]".PadRight(105, '-') + "{reset}\r\n{##UserCount}\r\n";
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
                    output += "{bold}{blue}Truespod Time {reset}".PadRight(48, ' ') + ": {blue}" + formatTimeNoZeros(TimeSpan.FromSeconds(ex.TrueSpodTime)) + "{reset}\r\n";
                    int[] rank = getRank(ex.UserName);
                    if (rank[0] > -1)
                        output += "{bold}{blue}Spodlist Rank {reset}".PadRight(48, ' ') + ": {blue}" + rank[0].ToString() + " (out of " + rank[1] + "){reset}\r\n";

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
                    if (ex.FacebookPage != "")
                        output += "{bold}{magenta}Facebook Page {reset}".PadRight(51, ' ') + ": {magenta}" + ex.FacebookPage + "{reset}\r\n";
                    if (ex.Twitter != "")
                        output += "{bold}{magenta}Twitter ID {reset}".PadRight(51, ' ') + ": {magenta}" + ex.Twitter + "{reset}\r\n";

                    for (int i = 0; i < ex.favourites.Count; i++)
                    {
                        if (ex.favourites[i].value != "" && ex.favourites[i].type != "")
                            output += ("{bold}{magenta}Favourite " + ex.favourites[i].type + " {reset}").PadRight(51, ' ') + ": {magenta}" + ex.favourites[i].value + "{reset}\r\n";
                    }
                    
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
                    output += "{bold}{yellow}Rank {reset}".PadRight(50, ' ') + ": " + ex.ColourUserName.Replace(ex.UserName, rankName(ex.PlayerRank)) + "{reset}\r\n";
                    output += "{bold}{yellow}Blocking Shouts {reset}".PadRight(50, ' ') + ": {yellow}" + (ex.HearShouts ? "No" : "Yes") + "{reset}\r\n";

                    if (ex.PlayerRank >= (int)Player.Rank.Staff)
                        output += "{bold}{yellow}Has ressed{reset}".PadRight(50, ' ') + ": {yellow}" + ex.ResCount.ToString() + " player" + (ex.ResCount == 1 ? "" : "s") + "{reset}\r\n";

                    Player.privs pPrivs = ex.SpecialPrivs;
                    //if (pPrivs.builder || pPrivs.tester || pPrivs.noidle || pPrivs.spod || pPrivs.minister)
                    if (pPrivs.anyPrivs)
                    {
                        output += "{bold}{yellow}Special Privs {reset}".PadRight(50, ' ') + ": {yellow}";
                        if (pPrivs.builder) output += "[builder]";
                        if (pPrivs.tester) output += "[tester]";
                        if (pPrivs.noidle) output += "[noidle]";
                        if (pPrivs.spod) output += "[spod]";
                        if (pPrivs.minister) output += "[minster]";
                        //output = output.Remove(output.Length - 1, 1) + "{reset}\r\n";
                        output += "{reset}\r\n";
                    }

                    output += "{bold}{yellow}On Channels {reset}".PadRight(50, ' ') + ": {yellow}" + getChannels(ex.UserName) + "{reset}\r\n";
                    if (ex.InformTag != "")
                        output += "{bold}{yellow}Inform Tag {reset}".PadRight(50, ' ') + ": {yellow}[" + ex.InformTag + "{reset}{yellow}]{reset}\r\n";

                    output += "{bold}{yellow}Marital Status {reset}".PadRight(50, ' ') + ": {yellow}";
                    if (ex.maritalStatus > Player.MaritalStatus.ProposedTo && ex.Spouse != "")
                        output += ex.maritalStatus.ToString() + (ex.maritalStatus == Player.MaritalStatus.Engaged || ex.maritalStatus == Player.MaritalStatus.Married ? " to " : (ex.maritalStatus == Player.MaritalStatus.Divorced ? " from " : " by ")) + ex.Spouse;
                    else
                        output += "Single";
                    output += "{reset}\r\n";

                    if (myPlayer.PlayerRank >= (int)Player.Rank.Staff)
                    {
                        output += "{bold}{red}Kicked {reset}".PadRight(47, ' ') + ": {red}" + ex.KickedCount.ToString() + "{reset}\r\n";
                        output += "{bold}{red}Warned {reset}".PadRight(47, ' ') + ": {red}" + ex.WarnedCount.ToString() + "{reset}\r\n";
                        output += "{bold}{red}Idled {reset}".PadRight(47, ' ') + ": {red}" + ex.IdledCount.ToString() + "{reset}\r\n";
                        output += "{bold}{red}Slapped {reset}".PadRight(47, ' ') + ": {red}" + ex.SlappedCount.ToString() + "{reset}\r\n";
                        output += "{bold}{red}Jailed {reset}".PadRight(47, ' ') + ": {red}" + ex.JailedCount.ToString() + "{reset}\r\n";
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

        public void cmdNoShout(string message)
        {
            if (myPlayer.HearShouts)
                sendToUser("You are now blocking shouts", true, false, false);
            else
                sendToUser("You are now hearing shouts again", true, false, false);
            myPlayer.HearShouts = !myPlayer.HearShouts;
            myPlayer.SavePlayer();
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

        public void cmdNoPrefix(string message)
        {
            sendToUser(myPlayer.SeePrefix ? "You are now ignoring prefixes" : "You are now seeing prefixes", true, false, false);
            myPlayer.SeePrefix = !myPlayer.SeePrefix;
            myPlayer.SavePlayer();
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
                        output += centerText("There " + (Server.playerCountToday == 1 ? "has " : "have ") + "been " + Server.playerCountToday + " player" + (Server.playerCountToday == 1 ? "" : "s") + " connected today") + "\r\n";
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

        public void doInform(bool login)
        {
            foreach (Connection c in connections)
            {
                if (c.socket.Connected && myPlayer != null && c.myPlayer != null && c.myPlayer.UserName != myPlayer.UserName && c.myState >= 10)
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

        #region getPlayers

        private static List<Player> getPlayers()
        {
            return getPlayers("*", false, false, false, false);
        }

        private static List<Player> getPlayers(bool staffOnly, bool builderOnly, bool testerOnly, bool gitsOnly)
        {
            return getPlayers("*", staffOnly, builderOnly, testerOnly, gitsOnly);
        }

        private static List<Player> getPlayers(string startsWith)
        {
            return getPlayers(startsWith, false, false, false, false);
        }

        private static List<Player> getPlayers(string startsWith, bool staffOnly, bool builderOnly, bool testerOnly, bool gitsOnly)
        {
            List<Player> list = new List<Player>();
            string path = Path.Combine(Server.userFilePath,(@"players" + Path.DirectorySeparatorChar));
            DirectoryInfo di = new DirectoryInfo(path);
            DirectoryInfo[] subs = di.GetDirectories(startsWith);

            FileInfo[] fi = di.GetFiles();
            foreach (FileInfo file in fi)
            {
                Player load = Player.LoadPlayer(file.Name.Replace(".xml",""),0);
                if (load != null && ((staffOnly && load.PlayerRank >= (int)Player.Rank.Guide) || (builderOnly && load.SpecialPrivs.builder) || (testerOnly && load.SpecialPrivs.tester) || (gitsOnly && (load.Git || load.AutoGit))) || (!staffOnly && !builderOnly && !testerOnly && !gitsOnly))
                    list.Add(load);
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (isOnline(list[i].UserName))
                {
                    foreach (Connection c in connections)
                    {
                        if (c.myPlayer.UserName.ToLower() == list[i].UserName.ToLower())
                            list[i] = c.myPlayer;
                    }
                }
            }
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


    }
}
