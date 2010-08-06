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

        private enum gender
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

        public message              editMail = new message();

        
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
                if (myPlayer.HourlyChime && DateTime.Now.Minute == 0 && DateTime.Now.Hour != lastHChimeHour && !myPlayer.InMailEditor)
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
                    if (socket.Connected)
                        line = Reader.ReadLine();
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
            line = cleanLine(line);
            if (myPlayer!=null)
                loadCommands();

            if (myState == 1)
            {
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
                            DirectoryInfo di = new DirectoryInfo(@"players" + Path.DirectorySeparatorChar);
                            DirectoryInfo[] dirs = null;
                            if (di.Exists)
                            {
                                dirs = di.GetDirectories();
                            }
                            if (dirs == null || dirs.Length == 0) // There are no player files!
                            {
                                newUser.createStatus = 1;
                                newUser.username = line.Trim();
                                myPlayer = Player.LoadPlayer(line.Trim(), myNum);
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
                                        bool reconnect = false;
                                        foreach (Connection conn in connections)
                                        {
                                            if (conn.myPlayer != null && conn.myPlayer.UserName != null && conn.myPlayer.UserName.ToLower() == line.Trim() && conn != this)
                                            {
                                                myPlayer = conn.myPlayer;
                                                conn.Disconnect();
                                                reconnect = true;
                                            }
                                        }

                                        //myState = 2;
                                        myState = 9;
                                        myPlayer.UserName = line.Trim();
                                        myPlayer.CurrentIP = connPoint;
                                        myPlayer.CurrentLogon = DateTime.Now;
                                        myPlayer.LastActive = DateTime.Now;
                                        //doPrompt();

                                        if (!reconnect)
                                        {
                                            myState = 2;
                                            string welcome = AnsiColour.Colorise(loadTextFile(@"files" + Path.DirectorySeparatorChar + "welcome.txt") + "{reset}");
                                            if (welcome != "")
                                            {
                                                sendToUser("{bold}{cyan}---[{red}Welcome{cyan}]".PadRight(103, '-') + "{reset}\r\n\r\n" + welcome + "\r\n{bold}{cyan}" + "".PadRight(80, '-') + "{reset}\r\nPress enter to continue");
                                            }
                                            foreach (Connection c in connections)
                                            {
                                                // Newbie notification ...
                                                if (c.myPlayer.PlayerRank >= (int)Player.Rank.Guide && c.myPlayer.OnDuty)
                                                    sendToUser("{bold}{green}[Newbie alert]{reset} " + myPlayer.UserName + " has just connected" + (c.myPlayer.PlayerRank >= (int)Player.Rank.Admin ? " from ip " + myPlayer.CurrentIP : ""), c.myPlayer.UserName, true, c.myPlayer.DoColour, false, false);
                                            }
                                            //sendToUser("New player!!", true, true, false);
                                            //sendToRoom(myPlayer.ColourUserName + " " + myPlayer.LogonMsg, "");
                                        }
                                        else
                                        {
                                            sendToRoom(myPlayer.ColourUserName + " " + "briefly phases out and back into existance");
                                        }

                                        Writer.Flush();
                                    }

                                    else
                                    {
                                        sendEchoOff();
                                        sendToUser("{bold}Please enter your password: {reset}", myPlayer.UserName, true, myPlayer.DoColour, false, false);
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
                                myPlayer.UserName = newUser.username;
                                myPlayer.Password = newUser.tPassword;
                                myPlayer.CurrentIP = connPoint;
                                myPlayer.CurrentLogon = DateTime.Now;
                                myPlayer.LastActive = DateTime.Now;
                                myPlayer.ResBy = "System";
                                myPlayer.PlayerRank = (int)Player.Rank.HCAdmin;
                                myPlayer.ResDate = DateTime.Now;
                                myPlayer.Description = "is da admin";
                                Player.privs p = new Player.privs();
                                p.builder = true;
                                p.tester = true;
                                p.noidle = true;
                                myPlayer.SpecialPrivs = p;

                                myState = 3;
                                sendEchoOn();
                                sendToUser("\r\nWelcome, " + myPlayer.ColourUserName + ". You are now the admin of the system", true);
                                myPlayer.SavePlayer();
                            }
                            else
                            {
                                sendToUser("\r\nPasswords do not match\r\nPlease enter a password: ", true, false, false);
                                newUser.createStatus = 1;
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
                        if (c.myPlayer.UserName.ToLower() == myPlayer.ResBy.ToLower())
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
                            if (conn.myPlayer != null && conn.myPlayer.UserName != null && conn.myPlayer.UserName.ToLower() == line.Trim() && conn != this)
                            {
                                myPlayer = conn.myPlayer;
                                conn.Disconnect();
                                reconnect = true;
                            }
                        }

                        if (!reconnect)
                        {
                            Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Login: " + myPlayer.UserName);
                            showMOTD(false);
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                                cmdCheckLogs("");
                            checkMail();

                            sendToUser("\r\nLast login " + myPlayer.LastLogon.ToShortDateString() + " from " + myPlayer.LastIP, true, false, false);
                            doWarnings();

                            myState = 10;
                            sendToRoom(myPlayer.UserName + " " + myPlayer.EnterMsg, "", myPlayer.UserRoom, myPlayer.UserName);
                            myPlayer.LoginCount++;
                            myPlayer.SavePlayer();
                        }
                        else
                            sendToRoom(myPlayer.ColourUserName + " " + "briefly phases out and back into existance");

                        myPlayer.CurrentIP = connPoint;
                        myPlayer.CurrentLogon = DateTime.Now;
                        myPlayer.LastActive = DateTime.Now;

                        doPrompt();
                    }
                }
                else
                {
                    sendToUser("{bold}{red}Password incorrect{white}\r\nPlease enter your password: {reset}", myPlayer.UserName, true, myPlayer.DoColour, false, false);
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
                else if (cmd != "")
                {
                    if (cmd.Substring(0, 1) == "#" && myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                    {
                        adminIdle = true;
                        cmd = cmd.Substring(1);
                    }

                    if (cmd.ToLower() == "quit")
                    {
                        Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] Logout: " + myPlayer.UserName);
                        sendToRoom(myPlayer.UserName + " " + myPlayer.LogoffMsg, "");
                        myPlayer.TotalOnlineTime += Convert.ToInt16((DateTime.Now - myPlayer.CurrentLogon).TotalSeconds);
                        myPlayer.LastLogon = DateTime.Now;
                        myPlayer.LastIP = myPlayer.CurrentIP;
                        int longCheck = (int)(DateTime.Now - myPlayer.CurrentLogon).TotalSeconds;
                        if (longCheck > myPlayer.LongestLogin) myPlayer.LongestLogin = longCheck;
                        myPlayer.SavePlayer();
                        socket.Shutdown(SocketShutdown.Both);
                    }
                    else
                    {
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
                                    sendToUser("Sorry, there has been an error", true);
                                }
                                finally
                                {
                                    found = true;
                                    if (!adminIdle)
                                        myPlayer.LastActive = DateTime.Now;
                                    doPrompt();
                                }
                            }
                        }
                        if (!found)
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
                                        sendToUser("Sorry, there has been an error", true);
                                    }
                                    finally
                                    {
                                        found = true;
                                        if (!adminIdle)
                                            myPlayer.LastActive = DateTime.Now;
                                        doPrompt();
                                    }
                                }
                            }
                        }
                        if (!found)
                            sendToUser("Huh?", true, true, false);
                    }
                }
                else
                {
                    doPrompt(myPlayer.UserName);
                }
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
                try
                {
                    conn.Writer.Write(AnsiColour.Colorise(msg, !conn.myPlayer.DoColour));
                }
                catch (Exception ex)
                {
                    logError(ex.ToString(), "Socket write");
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
                if (conn.myPlayer != null && conn.myPlayer.UserName.ToLower() == user.ToLower())
                {
                    try
                    {
                        string prefix = "";
                        if (conn.myPlayer != null && conn.lastSent == conn.myPlayer.Prompt && !msg.StartsWith(conn.myPlayer.Prompt) && conn.myPlayer.UserName != myPlayer.UserName)
                            prefix = "\r\n";
                        if (newline)
                            conn.Writer.WriteLine(prefix + AnsiColour.Colorise(msg, (removeColour || !conn.myPlayer.DoColour)));
                        else
                            conn.Writer.Write(prefix + AnsiColour.Colorise(msg, (removeColour || !conn.myPlayer.DoColour)));

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
                if (conn.myPlayer != null && conn.myPlayer.UserName != sender && conn.myPlayer.UserRoom == room && !conn.myPlayer.InMailEditor)
                {
                    sendToUser(msgToOthers, conn.myPlayer.UserName, newline, conn.myPlayer.DoColour, receiverPrompt, true);
                }
                else
                {
                    if (msgToSender != "")
                    {
                        sendToUser(msgToSender, sender, newline, myPlayer.DoColour, senderPrompt, true);
                    }
                }
                
            }
        }

        #endregion

        #region sendToStaff

        private void sendToStaff(string message, int rank, bool newline)
        {
            foreach (Connection conn in connections)
            {
                if (conn.myPlayer.PlayerRank >= rank && myPlayer.onStaffChannel((Player.Rank)rank) && !conn.myPlayer.InMailEditor)
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
                    if (chan.OnChannel(c.myPlayer.UserName) && !c.myPlayer.ClubChannelMute && !c.myPlayer.InMailEditor)
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
                return (textReader.ReadToEnd());
            }
            else
            {
                logError("Unable to load file " + path + " - file does not exist", "File I/O");
                return "";
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
            string path = (@"rooms" + Path.DirectorySeparatorChar);
            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                DirectoryInfo[] subs = di.GetDirectories();
                foreach (DirectoryInfo dir in subs)
                {
                    FileInfo[] fi = dir.GetFiles();
                    foreach (FileInfo file in fi)
                    {
                        Room load = Room.LoadRoom(file.Name.Replace(".xml", ""));
                        if (load != null)
                            list.Add(load);
                    }
                }
            }

            if (list.Count == 0 || list == null)
            {
                // There are no rooms, so we need to create a default one!
                Room newRoom = new Room();
                newRoom.shortName = "Main";
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
                string path = @"logs" + Path.DirectorySeparatorChar;
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
            string path = (@"logs" + Path.DirectorySeparatorChar);
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
            string path = (@"logs" + Path.DirectorySeparatorChar);
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
            string path = (@"logs" + Path.DirectorySeparatorChar);

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
                            string showLog = loadTextFile(@"logs" + Path.DirectorySeparatorChar + f.Name);
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
            string path = (@"logs" + Path.DirectorySeparatorChar);

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

        #endregion

        #region Methods

        #region Comms commands

        public void cmdSay(string message)
        {
            sendToRoom(myPlayer.ColourUserName + " " + sayWord(message, false) + " \"" + wibbleText(message, false) + "{reset}\"", "You " + sayWord(message, true) + " \"" + wibbleText(message, false) + "{reset}\"", myPlayer.UserRoom, myPlayer.UserName, true, false, true);
        }

        public void cmdThink(string message)
        {
            sendToRoom(myPlayer.ColourUserName + " thinks . o O ( " + wibbleText(message, false) + " {reset})", "You think . o O ( " + wibbleText(message, false) + " {reset})", false, true);
        }

        public void cmdSing(string message)
        {
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
                                if (c.myPlayer.InMailEditor)
                                    sendToUser(c.myPlayer.ColourUserName + " is editing at the moment and can't be disturbed", true, false, false);
                                else
                                {
                                    sendToUser("You " + tellWord(text) + c.myPlayer.UserName + " \"" + wibbleText(text, false) + "{reset}\"", true, false);
                                    sendToUser(">>" + myPlayer.ColourUserName + " " + tellWord(text, false) + "\"" + wibbleText(text, false) + "{reset}\"", c.myPlayer.UserName, true, c.myPlayer.DoColour, false, true);
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
                                if (c.myPlayer.InMailEditor)
                                    sendToUser(c.myPlayer.ColourUserName + " is editing at the moment and can't be disturbed", true, false, false);
                                else
                                {
                                    if (!text.StartsWith("'"))
                                        text = " " + text;

                                    sendToUser("You emote \"" + myPlayer.ColourUserName + wibbleText(text, true) + "{reset}\" to " + c.myPlayer.ColourUserName, true, false, true);
                                    sendToUser(">>" + myPlayer.ColourUserName + "{bold}{yellow}" + wibbleText(text, true) + "{reset}", matches[0], true, c.myPlayer.DoColour, false, true);
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
                                if (c.myPlayer.InMailEditor)
                                    sendToUser(c.myPlayer.ColourUserName + " is editing at the moment and can't be disturbed", true, false, false);
                                else
                                {
                                    if (!text.StartsWith("'"))
                                        text = " " + text;

                                    sendToUser("You sing o/~ " + wibbleText(text, true) + " {reset}o/~ to " + c.myPlayer.ColourUserName, true, false);
                                    sendToUser(">>" + myPlayer.ColourUserName + " sings o/~" + wibbleText(text, true) + " {reset}o/~ to you", matches[0], true, c.myPlayer.DoColour, false, true);
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
            if (!message.StartsWith("'"))
                message = " " + message;
            sendToRoom(myPlayer.UserName + wibbleText(message, true), "You emote: " + myPlayer.UserName + wibbleText(message, true), false, false);
        }

        public void cmdEcho(string message)
        {
            if (message == "")
                sendToUser("Syntax: echo <message>");
            else
            {
                foreach (Connection c in connections)
                {
                    if (!c.myPlayer.InMailEditor)
                    {
                        if (myPlayer.PlayerRank >= (int)Player.Rank.Admin || !c.myPlayer.SeeEcho)
                        {
                            sendToUser(wibbleText(message, false), c.myPlayer.UserName, true, c.myPlayer.DoColour, c.myPlayer.UserName == myPlayer.UserName ? false : true, true);
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
            if (myPlayer != null)
            {
                if (myPlayer.CanShout)
                {
                    foreach (Connection c in connections)
                    {
                        if (c.myPlayer != null && c.myPlayer.UserName != myPlayer.UserName)
                        {
                            if (c.myPlayer.HearShouts && !c.myPlayer.InMailEditor)
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

        public void cmdTellFriends(string message)
        {
            if (message == "")
                sendToUser("Syntax: tf <message>", true, false, false);
            else
            {
                int count = 0;
                foreach (Connection c in connections)
                {
                    if (myPlayer.friends.IndexOf(c.myPlayer.UserName) != -1)
                    {
                        count++;
                        if (!c.myPlayer.InMailEditor)
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
                    if (myPlayer.friends.IndexOf(c.myPlayer.UserName) != -1)
                    {
                        count++;
                        if (!c.myPlayer.InMailEditor)
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
                    if (temp.friends.IndexOf(myPlayer.UserName) == -1)
                        sendToUser("You are not on " + temp.UserName + "'s friends list", true, false, false);
                    else
                    {
                        int count = 0;
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer.UserName != myPlayer.UserName && (temp.friends.IndexOf(c.myPlayer.UserName) != -1 || c.myPlayer.UserName == temp.UserName))
                            {
                                count++;
                                if (!c.myPlayer.InMailEditor)
                                {
                                    c.sendToUser("\r\n{bold}{green}(To " + (c.myPlayer.UserName == temp.UserName ? "your" : temp.UserName + "'s") + " friends) " + myPlayer.UserName + " " + sayWord(split[1], false) + " \"" + split[1] + "\"", true, true, true);
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
                    if (temp.friends.IndexOf(myPlayer.UserName) == -1)
                        sendToUser("You are not on " + temp.UserName + "'s friends list", true, false, false);
                    else
                    {
                        int count = 0;
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer.UserName != myPlayer.UserName && (temp.friends.IndexOf(c.myPlayer.UserName) != -1 || c.myPlayer.UserName == temp.UserName))
                            {
                                count++;
                                if (!c.myPlayer.InMailEditor)
                                {
                                    c.sendToUser("\r\n{bold}{green}(To " + (c.myPlayer.UserName == temp.UserName ? "your" : temp.UserName + "'s") + " friends) " + myPlayer.UserName + (split[1].Substring(0, 1) == "'" ? "" : " ") + split[1], true, true, true);
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
                    output += (ex.Prefix + " " + ex.ColourUserName + " " + ex.Description).Trim() + "\r\n";
                    output += "{bold}{cyan}" + line + "{reset}\r\n";
                    
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

                    if (ex.EmailAddress != "")
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
                    output += "{bold}{yellow}Rank {reset}".PadRight(50, ' ') + ": " + ex.GetRankColour() + rankName(ex.PlayerRank) + "{reset}\r\n";
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
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
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
                        output += (conn.myPlayer.Prefix + " " + conn.myPlayer.ColourUserName + " " + conn.myPlayer.Description).Trim() + "\r\n";
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
                        output += (conn.myPlayer.Prefix + " " + conn.myPlayer.ColourUserName + " " + conn.myPlayer.Description).Trim() + "\r\n";
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
                sendToUser("Syntax: Edtime <player> <+/-> <hours>");
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
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
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
                        myPlayer.EmailAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Email address to " + split[1] : "You blank your Email Address", true, false, false);
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
                }
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
                myPlayer.Description = "";
                myPlayer.SavePlayer();
            }
            else
            {
                if (AnsiColour.Colorise(message, true).Length > 40)
                    sendToUser("Title too long, try again", true, false, false);
                else
                {
                    myPlayer.Description = message + "{reset}";
                    sendToUser("You change your title to read: " + myPlayer.Description, true, false, false);
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
                        if (c.myPlayer.UserName.ToLower() == target[0])
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
                sendToUser("Syntax: list <newbies/staff/tester/players" + (myPlayer.PlayerRank >= (int)Player.Rank.Staff ? "/ip/gits/objects/rooms" : "") + ">");
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
                                if (!c.myPlayer.Invisible)
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
                                if (c.myPlayer.NewPlayer)
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
                            sendToUser("Syntax: list <newbies/staff/tester/builder/players" + (myPlayer.PlayerRank >= (int)Player.Rank.Staff ? "/ip/gits/objects/rooms" : "") + ">", true, false, false);
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
                    if (c.myPlayer.Wibbled)
                        output += c.myPlayer.UserName + " {bold}{blue}(wibbled by " + c.myPlayer.WibbledBy + "){reset}\r\n";
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
                        if (c.myPlayer.UserName.ToLower() == target[0])
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
                            if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                            {
                                t = c.myPlayer;
                                online = true;
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
                                if (c.myPlayer.UserName == t.UserName)
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
                sendToUser("Syntax: remove <player>");
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
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
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
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
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
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
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
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            if (c.myPlayer.PlayerRank >= myPlayer.PlayerRank)
                                sendToUser("Trying to abuse a fellow staff member, eh?");
                            else
                            {
                                found = true;
                                c.myPlayer.Description = "";
                                sendToUser("Your description has been removed by " + myPlayer.ColourUserName, c.myPlayer.UserName, true, c.myPlayer.DoColour, true, false);
                                sendToUser("You remove " + c.myPlayer.UserName + "'s description", true, false, false);
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
            string path = (@"dump" + Path.DirectorySeparatorChar);
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
            sendToUser(count.ToString() + " e-mail address" + (count == 1 ? "" : "es") + " dumped to file", true, false, false);
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
                sendToUser("Syntax: rename <player> <new name>");
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

                    // Iterate through and change To and From as required in messages
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



                    if (isOnline(target[0]))
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
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
                sendToUser("Syntax: itag <player> <inform tag>");
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
                            if (c.myPlayer.UserName == target[0])
                            {
                                sendToUser(tag == "" ? "You remove " + c.myPlayer.UserName + "'s inform tag" : "You set " + c.myPlayer.UserName + "'s inform tag to: " + tag, true, false, false);
                                if (!c.myPlayer.InMailEditor)
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
                                if (c.myPlayer.UserName == target[0])
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

        #region Friends stuff

        public void cmdFriend(string message)
        {
            if (message == "")
            {
                string output = "";
                if (myPlayer.friends.Count == 0)
                {
                    output = "You have no friends";
                }
                else
                {
                    int tabcount = 1;
                    foreach (string friend in myPlayer.friends)
                    {
                        output += ((tabcount++ +1)% 4 == 0 ? "\r\n" : "") + friend.PadRight(20, ' ');
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
                    if (myPlayer.friends.IndexOf(target[0]) == -1)
                    {
                        // Not already in friends list
                        myPlayer.friends.Add(target[0]);
                        sendToUser(target[0] + " added to your friends list", true, false, false);
                        if (isOnline(target[0]))
                            sendToUser(myPlayer.UserName + " has made you " + getGender("poss") + " friend", target[0], true, false, true, false);
                        myPlayer.SavePlayer();
                    }
                    else
                    {
                        myPlayer.friends.Remove(target[0]);
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
                        if (c.myPlayer.UserName.ToLower() == target[0])
                        {
                            sendToUser(c.myPlayer.ColourUserName + " is " + (c.myPlayer.Hidden ? "hiding" : "in " + getRoomFullName(c.myPlayer.UserRoom)), true, false, false);
                        }
                    }
                }
            }
            else
            {
                string output = "";
                foreach (Connection c in connections)
                {
                    if (c.myPlayer.UserName == myPlayer.UserName)
                        output += "You are " + (myPlayer.Hidden || myPlayer.Invisible ? "hiding " : "") + "in " + getRoomFullName(myPlayer.UserRoom) + "\r\n";
                    else if (!c.myPlayer.Invisible && (!c.myPlayer.Hidden || myPlayer.PlayerRank >= (int)Player.Rank.Admin))
                        output += c.myPlayer.ColourUserName + " is in " + getRoomFullName(c.myPlayer.UserRoom) + (c.myPlayer.Hidden ? " (hidden)" : "") + "\r\n";
                    else if (!c.myPlayer.Invisible)
                        output += c.myPlayer.ColourUserName + " is hiding\r\n";
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
                for (int i = 0; i < room.exits.Length; i++)
                {
                    if (room.exits[i] != "")
                        output += (Room.Direction)i + " -> " + getRoomFullName(room.exits[i]) + "\r\n";
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
                    for (int i = 0; i < currentRoom.exits.Length; i++)
                    {
                        if (((Room.Direction)i).ToString().ToLower() == message.ToLower() && currentRoom.exits[i] != "")
                        {
                            found = true;
                            movePlayer(currentRoom.exits[i]);
                        }
                        else if (currentRoom.exits[i].ToLower().StartsWith(message.ToLower()))
                        {
                            found = true;
                            movePlayer(currentRoom.exits[i]);
                        }
                    }
                    if (!found)
                    {
                        sendToUser("No such exit", true, false, false);
                    }
                }
                else
                {
                    sendToUser("Strange. You don't seem to be in a room at all ...", true, false, false);
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
            }
            else
                sendToUser("Strange, you don't seem to be anywhere!", true, false, false);
        }

        public void doWhoRoom()
        {
            string output = "";
            foreach (Connection c in connections)
            {
                if (c.myPlayer.UserName != myPlayer.UserName && c.myPlayer.UserRoom == myPlayer.UserRoom)
                    output += ", " + c.myPlayer.ColourUserName;
            }
            if (output == "")
                sendToUser("You are here by yourself\r\n", true, false, false);
            else
                sendToUser("The following people are here:\r\n\r\n" + output.Substring(2) + "\r\n", true, false, false);
        }

        public void movePlayer(string room)
        {

        }

        #endregion

        #region Messaging System

        public void cmdMessage(string message)
        {
            messages = loadMessages();

            if (message == "" || message.IndexOf(" ") == -1)
                sendToUser("Syntax: message <player" + (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? "/all/allstaff/admin/staff/guide" : "") + "> <message>");
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
                string path = @"messages" + Path.DirectorySeparatorChar;
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
            string path = @"messages" + Path.DirectorySeparatorChar;
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

        #endregion

        #region Mail System

        public void cmdMail(string message)
        {
            mail = loadMails();
            // Monster mailing system
            if (message == "")
                sendToUser("Syntax: mail <list/read/send/reply/del>");
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
                            sendToUser("Syntax: mail send <player> <subject>");
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
                                myPlayer.InMailEditor = true;
                                sendToUser("Now entering mail editor. Type \".help\" for a list of editor commands", true, false, false);
                                editMail = new message();
                                editMail.From = myPlayer.UserName;
                                editMail.To = target[0];
                                editMail.Subject = split[1];
                                editMail.Date = DateTime.Now;
                                editMail.Read = false;
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
                        sendToUser("Syntax: mail <list/read/send/reply/del>");
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
                string path = @"mail" + Path.DirectorySeparatorChar;
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
            string path = @"mail" + Path.DirectorySeparatorChar;
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
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            found = true;
                            sendToUser("{bold}{red}{bell} YYou have just been slapped by " + myPlayer.ColourUserName + "{bold}{red}!{reset}", c.myPlayer.UserName, true, c.myPlayer.DoColour, true, true);
                            foreach (Connection conn in connections)
                            {
                                if (conn.myPlayer.UserName != c.myPlayer.UserName)
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
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
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
                sendToUser("Syntax: warn <player> <warning>");
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
                            if (c.myPlayer.UserName == target[0])
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
                            if (c.myPlayer.UserName == target[0])
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
                            if (c.myPlayer.UserName == target[0])
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
            string path = (@"players" + Path.DirectorySeparatorChar);
            DirectoryInfo di = new DirectoryInfo(path);
            DirectoryInfo[] subs = di.GetDirectories(startsWith);
            foreach (DirectoryInfo dir in subs)
            {
                FileInfo[] fi = dir.GetFiles();
                foreach (FileInfo file in fi)
                {
                    Player load = Player.LoadPlayer(file.Name.Replace(".xml",""),0);
                    if (load != null && ((staffOnly && load.PlayerRank >= (int)Player.Rank.Guide) || (builderOnly && load.SpecialPrivs.builder) || (testerOnly && load.SpecialPrivs.tester) || (gitsOnly && (load.Git || load.AutoGit))) || (!staffOnly && !builderOnly && !testerOnly && !gitsOnly))
                        list.Add(load);
                }
            }
            return list;
        }

        private List<Player> getPlayers(int rank, bool singleRankOnly)
        {
            List<Player> list = new List<Player>();
            string path = (@"players" + Path.DirectorySeparatorChar);
            DirectoryInfo di = new DirectoryInfo(path);
            DirectoryInfo[] subs = di.GetDirectories();
            foreach (DirectoryInfo dir in subs)
            {
                FileInfo[] fi = dir.GetFiles();
                foreach (FileInfo file in fi)
                {
                    Player load = Player.LoadPlayer(file.Name.Replace(".xml", ""), 0);

                    if (load != null && (load.PlayerRank == rank || (load.PlayerRank > rank && !singleRankOnly)))
                        list.Add(load);
                }
            }
            return list;
        }

        #endregion

        #region Rooms

        private string getRoomFullName(string room)
        {
            string ret = "";
            foreach (Room r in roomList)
            {
                if (r.shortName == room)
                    ret = r.fullName;
            }
            return ret;
        }

        private Room getRoom(string room)
        {
            Room ret = null;
            foreach (Room r in roomList)
            {
                if (r.shortName == room)
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
                sendToUser("Syntax: ccinfo <channel>");
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
                            if (conn.myPlayer.UserName.ToLower() == c.Owner.ToLower() && !conn.myPlayer.ClubChannelMute)
                                users += ", " + conn.myPlayer.ColourUserName;
                        }
                    }
                    foreach (string user in c.Users)
                    {
                        foreach (Connection conn in connections)
                        {
                            if (conn.myPlayer.UserName.ToLower() == user.ToLower() && !conn.myPlayer.ClubChannelMute)
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
                sendToUser("Syntax: cu <channel> <message>");
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
                sendToUser("Syntax: ct <channel> <message>");
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
                sendToUser("Syntax: cs <channel> <message>");
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
                sendToUser("Syntax: ce <channel> <message>");
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
                sendToUser(myPlayer.InMailEditor ? "> " : myPlayer.Prompt.Replace("%t", DateTime.Now.ToShortTimeString()).Replace("%d", DateTime.Now.ToShortDateString()), false, false, false);
            else
            {
                foreach (Connection c in connections)
                {
                    if (c.myPlayer.UserName == user)
                    {
                        sendToUser(c.myPlayer.InMailEditor ? "> " :  c.myPlayer.Prompt.Replace("%t", DateTime.Now.ToShortTimeString()).Replace("%d", DateTime.Now.ToShortDateString()), c.myPlayer.UserName, false, c.myPlayer.DoColour, false, false);
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
                if (c.myPlayer != null && pNames.IndexOf(c.myPlayer.UserName) < 0 && c.myPlayer.UserName.ToLower().StartsWith(name.ToLower()))
                    pNames.Add(c.myPlayer.UserName);
            }

            return pNames.ToArray();
        }

        private bool isOnline(string username)
        {
            bool found = false;
            foreach (Connection c in connections)
            {
                if (c.myPlayer.UserName.ToLower() == username.ToLower())
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
                    c.Writer.Flush();
                }
            }
        }


        #endregion

    }
}
