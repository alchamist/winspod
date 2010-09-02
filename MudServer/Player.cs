using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.Security.Cryptography;

namespace MudServer
{
    public class Player
    {
        public enum Rank
        {
            Newbie,
            Member,
            Guide,
            Staff,
            Admin,
            HCAdmin
        }

        public enum ShowTo
        {
            Public,
            Friends,
            Private
        }

        public struct privs
        {
            public bool builder;
            public bool tester;
            public bool noidle;
        }

        public struct alias
        {
            public string aliasName;
            public string aliasCommand;
        }

        public struct playerList
        {
            public string name;
            public bool friend;
            public bool find;
            public bool noisy;
            public bool ignore;
            public bool inform;
            public bool grabme;
            public bool bar;
            public bool beep;
            public bool block;
            public bool mailblock;
        }

        public struct inventory
        {
            public string name;
            public int count;
            public bool wielded;
        }

        #region attributes

        private string      username;                                       // Players username
        private string      password;                                       // Players password - MD5 hashed

        #region system stuff

        private string      room = "Main";                                  // Their current room
        private bool        newplayer = true;                               // Are they a new, unsaved player?
        private DateTime    resDate;                                        // Date player was rezzed
        private string      resBy;                                          // Staff member who rezzed them
        private int         resCount;                                       // Number of people they have rezzed themselves
        private DateTime    lastLogon;                                      // Time/Date of their last logon
        private DateTime    currentLogon;                                   // Time/Date of their current logon
        private DateTime    lastActive;                                     // Time/Date of their last activity
        private int         longestLogin = 0;                               // Longest login time in seconds
        private int         totalOnlineTime;                                // Total time online in seconds;
        private double      averageLogin;                                   // Average login time
        private string      lastAddress;                                    // The last IP address they logged on from
        private string      currentAddress;                                 // The current IP address they are logged on from
        private privs       systemprivs;                                    // The user privs
        private bool        away;                                           // Is the user afk?
        
        #endregion

        #region movement messages

        private string      logonMessage = "arrives, dazed and confused";   // The message the room gets when they log on
        private string      logoffMessage = "leaves for normality";         // The message the room gets when they log off
        private string      enterMessage = "walks into the room";           // Enter message for when they enter a room
        private string      exitMessage = "wanders off into the distance";  // Exit message for when they leave a room
        private string      informTag = "";                                 // Settable inform tag to send to through notify system
        private string      logonScript = "";                               // Logon script

        #endregion

        #region user RL details

        private string      realName = "";                                  // Their real name
        private string      title = "is a newbie, be nice";                 // Their title
        private string      description = "";                               // Their description
        private string      tagline = "";                                   // Their tagline
        private string      email = "";                                     // Their e-mail address
        private int         emailPermissions = (int)ShowTo.Friends;         // Who we show e-mail address to
        private string      jabber = "";                                    // Their Jabber ID
        private string      icq = "";                                       // Their ICQ number
        private string      msn = "";                                       // Their MSN ID
        private string      yahoo = "";                                     // Their Yahoo ID
        private string      skype = "";                                     // Their Skype ID
        private DateTime    dateOfBirth;                                    // Their DOB
        private string      homeURL = "";                                   // Their home URL
        private string      workURL = "";                                   // Their work URL
        private string      occupation = "";                                // Their occupation
        private string      hometown = "";                                  // Their home town
        private int         jetlag;                                         // Their time difference

        #endregion

        #region user settings

        private string      prefix = "";                                    // Their prefix
        private bool        seePrefix = true;                               // Are they seeing prefixes?
        private int         gender = 0;                                     // Gender - 0 = neutral, 1 = Male, 2 = Female
        private string      logonRoom;                                      // Where they have set home to for logon
        private bool        colourEnable = true;                            // If they have colour enabled or not
        private Rank        rank = Rank.Newbie;                             // Player Rank - Newbie = non rezzed player
        private bool        invisible = false;                              // Invisibility for Admin
        private bool        guideChan = true;                               // On/Off guide channel
        private bool        staffChan = true;                               // On/Off staff channel
        private bool        adminChan = true;                               // On/Off admin channel
        private bool        hcAdminChan = true;                             // On/Off admin channel
        private bool        onDuty = true;                                  // On/Off duty - global mute/unmute for staff chans
        private bool        clubChanMute = false;                           // Have they muted the club channels?
        private bool        seeEchoFrom = false;                            // Does the user see who the echo is from
        private bool        hidden = false;                                 // Is the user hidden from "where" command
        private bool        hourlyChime = false;                            // Does the user want an hourly chime?

        #endregion

        #region user stats

        private int         kicked;                                         // Number of times kicked off
        private int         idled;                                          // Number of times idled out
        private int         warned;                                         // Number of times they have been warned
        private bool        hearShout = true;                               // Can they hear shouts?
        private bool        canShout = true;                                // Can they shout?
        private string      prompt = AppSettings.Default.TalkerName + ">";  // Their own personal prompt
        private bool        wibbled;                                        // Has the user been wibbled?
        private string      wibbledBy;                                      // Who wibbled them
        private bool        isGit = false;                                  // Is the user a git?
        private bool        isAutoGit = false;                              // Has the user been auto-gitted for too many warnings?
        private int         slapped;                                        // Number of times a player has been slapped
        private int         logons;                                         // Number of times they have logged on

        #endregion

        private bool        inMailEditor = false;                           // Are they in the editor?
        private bool        inDescriptionEditor = false;                    // Are they editing their description?

        private bool        inRoomEditor = false;                           // Are they editing a room description?
        private int         maxRooms = 3;                                   // How many rooms are they allowed?

        private bool        informAll;                                      // Do they want to be notified when anyone logs on/off?
        private bool        informFriends;                                  // Are they being informed of everyone?

        //public List<string> friends = new List<string>();                   // Friends list - set to public so can be manipulated
        //public List<string> informList = new List<string>();                // Inform list - set to public so can be manipulated

        private List<playerList> myList = new List<playerList>();           // Friends/Inform/Bar/etc list;
        private int         myListMaxSize = 20;                             // How many people can we have on the list?
        private playerList  allFriendsList = new playerList();              // Settings for all friends
        private playerList  allStaffList = new playerList();                // Settings for all staff
        private playerList  allPlayersList = new playerList();               // Settings for everyone

        private List<alias> aliasList = new List<alias>();                  // Alias commands

        private List<inventory> inventoryList = new List<inventory>();      // Inventory list!
        private int         maxWeight = 50;                                 // Maximum weight they can carry

        #endregion

        #region Setters & Getters

        public string UserName
        {
            get { return username; }
            set { username = value; }
        }

        public string ColourUserName
        {
            get {
                if (this.rank == Rank.HCAdmin)
                    return AppSettings.Default.HCAdminColour + username + "{reset}";
                else if (this.rank == Rank.Admin)
                    return AppSettings.Default.AdminColour + username + "{reset}";
                else if (this.rank == Rank.Staff)
                    return AppSettings.Default.StaffColour + username + "{reset}";
                else if (this.rank == Rank.Guide)
                    return AppSettings.Default.GuideColour + username + "{reset}";
                else
                    return username;
            }
        }

        public string UserRoom
        {
            get { return room; }
            set { room = value; }
        }

        public string EnterMsg
        {
            get { return enterMessage; }
            set { enterMessage = value; }
        }

        public string ExitMsg
        {
            get { return exitMessage; }
            set { exitMessage = value; }
        }

        public string LogonMsg
        {
            get { return logonMessage; }
            set { logonMessage = value; }
        }

        public string LogoffMsg
        {
            get { return logoffMessage; }
            set { logoffMessage = value; }
        }

        public string LogonRoom
        {
            get { return logonRoom; }
            set { logonRoom = value; }
        }

        public string Password
        {
            get { return password; }
            set { password = value; }
        }

        public bool NewPlayer
        {
            get { return newplayer; }
            set { newplayer = value; }
        }

        public int PlayerRank
        {
            get { return (int)rank; }
            set { rank = (Rank)value; }
        }

        public bool DoColour
        {
            get { return colourEnable; }
            set { colourEnable = value; }
        }

        public bool Invisible
        {
            get { return (rank >= Rank.Admin) ? invisible : false; }
            set { if (rank >= Rank.Admin) invisible = value; }
        }

        public bool Hidden
        {
            get { return hidden; }
            set { hidden = value; }
        }

        public string Prompt
        {
            get { return prompt.Trim() + " "; }
            set { prompt = value.Trim(); }
        }

        public bool Wibbled
        {
            get { return wibbled; }
            set { wibbled = value; }
        }

        public string WibbledBy
        {
            get { return wibbledBy; }
            set { wibbledBy = value; }
        }

        public bool CanShout
        {
            get { return canShout; }
            set { canShout = value; }
        }

        public bool HearShouts
        {
            get { return hearShout; }
            set { hearShout = value; }
        }

        public string Prefix
        {
            get { return prefix; }
            set { prefix = value; }
        }

        public bool SeePrefix
        {
            get { return seePrefix; }
            set { seePrefix = value; }
        }

        public bool HourlyChime
        {
            get { return hourlyChime; }
            set { hourlyChime = value; }
        }

        public string Title
        {
            get { return title; }
            set { title = value; }
        }

        public string Description
        {
            get { return description; }
            set { description = value; }
        }

        public string Tagline
        {
            get { return tagline; }
            set { tagline = value; }
        }

        public string LastIP
        {
            get { return lastAddress; }
            set { lastAddress = value; }
        }

        public string CurrentIP
        {
            get { return currentAddress; }
            set { currentAddress = value; }
        }

        public DateTime CurrentLogon
        {
            get { return currentLogon; }
            set { lastLogon = currentLogon; currentLogon = value; }
        }

        public DateTime LastLogon
        {
            get { return lastLogon; }
            set { lastLogon = value; }
        }

        public int LongestLogin
        {
            get { return longestLogin; }
            set { if (value > longestLogin) longestLogin = value; }
        }

        public int LoginCount
        {
            get { return logons ; }
            set { logons = value; }
        }

        public TimeSpan AverageLoginTime
        {
            get { if (logons > 0) return (DateTime.Now.AddSeconds(totalOnlineTime / logons) - DateTime.Now); else return new TimeSpan(); }
        }

        public int TotalOnlineTime
        {
            get { return totalOnlineTime; }
            set { totalOnlineTime = value; }
        }

        public DateTime ResDate
        {
            get { return resDate; }
            set { resDate = value; }
        }

        public string ResBy
        {
            get { return resBy; }
            set { resBy = value; }
        }

        public int ResCount
        {
            get { return resCount; }
            set { resCount = value; }
        }

        public string EmailAddress
        {
            get { return email; }
            set { email = value; }
        }

        public int EmailPermissions
        {
            get { return emailPermissions; }
            set { emailPermissions = value; }
        }

        public string JabberAddress
        {
            get { return jabber; }
            set { jabber = value; }
        }

        public string ICQAddress
        {
            get { return icq; }
            set { icq = value; }
        }

        public string MSNAddress
        {
            get { return msn; }
            set { msn = value; }
        }

        public string YahooAddress
        {
            get { return yahoo; }
            set { yahoo = value; }
        }

        public string SkypeAddress
        {
            get { return skype; }
            set { skype = value; }
        }

        public string HomeURL
        {
            get { return homeURL; }
            set { homeURL = value; }
        }

        public string WorkURL
        {
            get { return workURL; }
            set { workURL = value; }
        }

        public string RealName
        {
            get { return realName; }
            set { realName = value; }
        }

        public string Occupation
        {
            get { return occupation; }
            set { occupation = value; }
        }

        public string Hometown
        {
            get { return hometown; }
            set { hometown = value; }
        }

        public int JetLag
        {
            get { return jetlag; }
            set { jetlag = value; }
        }

        public int Gender
        {
            get { return gender; }
            set { gender = value; }
        }

        public int KickedCount
        {
            get { return kicked; }
            set { kicked = value; }
        }

        public int IdledCount
        {
            get { return idled; }
            set { idled = value; }
        }

        public int WarnedCount
        {
            get { return warned; }
            set { warned = value; }
        }

        public int SlappedCount
        {
            get { return slapped; }
            set { slapped = value; }
        }

        public privs SpecialPrivs
        {
            get { return systemprivs; }
            set { systemprivs = value; }
        }

        public DateTime DateOfBirth
        {
            get { return dateOfBirth; }
            set { dateOfBirth = value; }
        }

        public bool Away
        {
            get { return away; }
            set { away = value; }
        }

        public DateTime LastActive
        {
            get { return lastActive; }
            set { lastActive = value; }
        }

        public bool OnGuide
        {
            get { return guideChan; }
            set { guideChan = value; }
        }

        public bool OnStaff
        {
            get { return staffChan; }
            set { staffChan = value; }
        }

        public bool OnAdmin
        {
            get { return adminChan; }
            set { adminChan = value; }
        }

        public bool OnHCAdmin
        {
            get { return hcAdminChan; }
            set { hcAdminChan = value; }
        }

        public bool OnDuty
        {
            get { return onDuty; }
            set { onDuty = value; }
        }

        public bool Git
        {
            get { return isGit; }
            set { isGit = value; }
        }

        public bool AutoGit
        {
            get { return isAutoGit; }
            set { isAutoGit = value; }
        }

        public bool SeeEcho
        {
            get { return seeEchoFrom; }
            set { seeEchoFrom = value; }
        }

        public bool ClubChannelMute
        {
            get { return clubChanMute; }
            set { clubChanMute = value; }
        }

        public int MyListMaxSize
        {
            get { return myListMaxSize; }
            set { myListMaxSize = value; }
        }

        public List<playerList> MyList
        {
            get { return myList; }
        }

        public bool InformAll
        {
            get { return informAll; }
            set { informAll = value; }
        }

        public bool InformFriends
        {
            get { return informFriends; }
            set { informFriends = value; }
        }

        public string InformTag
        {
            get { return informTag; }
            set { informTag = value; }
        }

        public string LogonScript
        {
            get { return logonScript; }
            set { logonScript = value; }
        }

        public bool InMailEditor
        {
            get { return inMailEditor; }
            set { inMailEditor = value; }
        }

        public bool InDescriptionEditor
        {
            get { return inDescriptionEditor; }
            set { inDescriptionEditor = value; }
        }

        public bool InRoomEditor
        {
            get { return inRoomEditor; }
            set { inRoomEditor = value; }
        }

        public bool InEditor
        {
            get { return inMailEditor || inDescriptionEditor || inRoomEditor; }
        }

        public List<alias> AliasList
        {
            get { return aliasList; }
        }

        public List<string> FriendsArray
        {
            get
            {
                List<string> ret = new List<string>();
                foreach (playerList p in myList)
                {
                    if (p.friend)
                        ret.Add(p.name);
                }
                return ret;
            }
        }

        public int MaxRooms
        {
            get { return maxRooms; }
            set { maxRooms = value; }
        }

        public List<inventory> Inventory
        {
            get { return inventoryList; }
        }

        public int MaxWeight
        {
            get { return maxWeight; }
            set { maxWeight = value; }
        }

        #endregion

        #region Constructor/Destructor

        public Player()
        {
            //username = "Wibble" + userConn.ToString();
        }

        ~Player()
        {
            //if (rank!=Rank.Newbie)
                //SavePlayer();
        }

        #endregion

        #region Load/Save

        public bool SavePlayer()
        {
            if (this.PlayerRank > (int)Rank.Newbie)
            {
                bool ret = false;
                try
                {
                    //string path = @"players\" + this.username.Substring(0, 1) + @"\";
                    string path = @"players" + Path.DirectorySeparatorChar + this.username.Substring(0, 1).ToUpper() + Path.DirectorySeparatorChar;
                    string fname = this.username + ".xml";
                    string fpath = path + fname;
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);

                    //if (!File.Exists(fpath)) File.Create(path);
                    XmlSerializer serial = new XmlSerializer(typeof(Player));
                    TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                    serial.Serialize(textWriter, this);
                    textWriter.Close();
                    ret = true;
                }
                catch (Exception ex)
                {
                    Connection.logError(ex.ToString(), "filesystem");
                }
                return ret;
            }
            else
                return false;
        }

        public static Player LoadPlayer(string name, int userConn)
        {
            Player load;
            //string path = (@"players\" + name.Substring(0, 1) + @"\" + name.ToLower() + ".xml").ToLower();
            string path = ("players" + Path.DirectorySeparatorChar + name.Substring(0, 1).ToUpper() + Path.DirectorySeparatorChar + name.ToLower() + ".xml");

            Debug.Print(Path.GetFullPath(path));

            if (File.Exists(path))
            {
                try
                {
                    XmlSerializer deserial = new XmlSerializer(typeof(Player));
                    TextReader textReader = new StreamReader(@path);
                    load = (Player)deserial.Deserialize(textReader);
                    textReader.Close();
                    load.NewPlayer = false;
                }
                catch (Exception e)
                {
                    Debug.Print(e.ToString());
                    load = null;
                }
            }
            else
            {
                load = new Player();
                load.NewPlayer = true;
            }
            
            return load;

        }

        public static void RemovePlayerFile(string name)
        {
            string path = ("players" + Path.DirectorySeparatorChar + name.Substring(0, 1).ToUpper() + Path.DirectorySeparatorChar + name.ToLower() + ".xml");

            Debug.Print(Path.GetFullPath(path));

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
        }

        #endregion

        #region Password Stuff

        public static string md5Encrypt(string inputString)
        {
            byte[] input = Encoding.UTF8.GetBytes(inputString);
            byte[] output = MD5.Create().ComputeHash(input);
            return Convert.ToBase64String(output);
        }

        public bool checkPassword(string testPassword)
        {
            return (testPassword == password);
        }

        #endregion

        #region methods

        public string GetRankColour()
        {
            string ret;
            switch (this.rank)
            {
                case Rank.HCAdmin:
                    ret = AppSettings.Default.HCAdminColour;
                    break;
                case Rank.Admin:
                    ret = AppSettings.Default.AdminColour;
                    break;
                case Rank.Staff:
                    ret = AppSettings.Default.StaffColour;
                    break;
                case Rank.Guide:
                    ret = AppSettings.Default.GuideColour;
                    break;
                default:
                    ret = "";
                    break;
            }
            return ret;
        }

        public int AddLogon()
        {
            return ++logons;
        }

        public bool onStaffChannel(Rank r)
        {
            return onDuty && ((r == Rank.Guide && guideChan) || (r == Rank.Staff && staffChan) || (r == Rank.Admin && adminChan) || (r == Rank.HCAdmin && hcAdminChan));
        }

        public bool InformFor(string username)
        {
            bool ret = false;
            if (allPlayersList.inform)
                ret = true;
            else if (allFriendsList.inform && isFriend(username))
                ret = true;
            else if (allStaffList.inform)
            {
                Player p = Player.LoadPlayer(username, 0);
                if (p.PlayerRank >= (int)Rank.Guide)
                    ret = true;
            }
            else
            {
                foreach (playerList p in myList)
                {
                    if (p.name.ToLower() == username.ToLower() && p.inform)
                        ret = true;
                }
            }
            return ret;
        }

        public void CleanMyList()
        {
            for (int i = 0; i < myList.Count; i++)
            {
                if (!myList[i].bar && !myList[i].beep && !myList[i].block && !myList[i].find && !myList[i].friend && !myList[i].grabme && !myList[i].ignore && !myList[i].inform && !myList[i].mailblock && !myList[i].noisy)
                {
                    myList.RemoveAt(i);
                }
            }
        }

        public bool SetInform(string username)
        {
            bool ret = false;
            for (int i = 0; i < myList.Count(); i++)
            {
                if (myList[i].name.ToLower() == username.ToLower())
                {
                    ret = true;
                    playerList temp = myList[i];
                    temp.inform = true;
                    myList[i] = temp;
                }
            }
            if (!ret)
            {
                if (myList.Count > myListMaxSize)
                {
                    ret = true;
                    playerList temp = new playerList();
                    temp.name = username;
                    temp.inform = true;
                    myList.Add(temp);
                }
            }
            return ret;
        }

        public bool RemoveInform(string username)
        {
            bool ret = false;
            for (int i = 0; i < myList.Count(); i++)
            {
                if (myList[i].name.ToLower() == username.ToLower())
                {
                    ret = true;
                    playerList temp = myList[i];
                    temp.inform = false;
                    myList[i] = temp;
                }
            }
            CleanMyList();
            return ret;
        }

        public bool isFriend(string username)
        {
            bool ret = false;
            foreach(playerList p in myList)
            {
                if (p.name.ToLower() == username.ToLower() && p.friend)
                    ret = true;
            }
            return ret;
        }

        public bool AddFriend(string username)
        {
            if (myList.Count >= myListMaxSize)
                return false;
            else
            {
                bool found = false;
                for (int i = 0; i < myList.Count; i++)
                {
                    if (myList[i].name.ToLower() == username.ToLower())
                    {
                        found = true;
                        playerList temp = myList[i];
                        temp.friend = true;
                        myList[i] = temp;
                    }
                }
                if (!found)
                {
                    playerList temp = new playerList();
                    temp.name = username;
                    temp.friend = true;
                    myList.Add(temp);
                }
                return true;
            }
        }

        public void RemoveFriend(string username)
        {
            for (int i = 0; i < myList.Count; i++)
            {
                if (myList[i].name.ToLower() == username.ToLower())
                {
                    //playerList temp = myList[i];
                    //myList[i] = temp;
                    myList.RemoveAt(i);
                }
            }
            CleanMyList();
        }

        public void AddToInventory(string objectName)
        {
            for (int i = 0; i < inventoryList.Count; i++)
            {
                inventory temp = inventoryList[i];
                if (temp.name.ToLower() == objectName.ToLower())
                {
                    temp.count++;
                    inventoryList[i] = temp;
                    SavePlayer();
                    return;
                }
            }
            inventory newInv = new inventory();
            newInv.name = objectName;
            newInv.count = 1;
            inventoryList.Add(newInv);
            SavePlayer();
        }

        public void RemoveFromInventory(string objectName)
        {
            if (inventoryList.Count > 0)
            {
                for (int i = inventoryList.Count - 1; i >= 0; i--)
                {
                    if (inventoryList[i].name.ToLower() == objectName.ToLower())
                    {
                        if (inventoryList[i].count == 1)
                        {
                            inventoryList.RemoveAt(i);
                        }
                        else
                        {
                            inventory temp = inventoryList[i];
                            temp.count--;
                            inventoryList[i] = temp;
                        }
                        SavePlayer();
                    }
                }
            }
        }

        public int InInventory(string objectName)
        {
            foreach (inventory i in inventoryList)
            {
                if (i.name.ToLower() == objectName.ToLower())
                    return i.count;
            }
            return 0;
        }

        public void WieldInventory(string objectName)
        {
            for (int i = 0; i < inventoryList.Count; i++)
            {
                inventory temp = inventoryList[i];
                if (temp.name.ToLower() == objectName.ToLower())
                {
                    temp.wielded = !temp.wielded;
                }
                else
                {
                    temp.wielded = false;
                }
                inventoryList[i] = temp;
            }
        }


        #region Alias Stuff

        public void AddAlias(string aliasName, string aliasCommand)
        {
            alias a = new alias();
            a.aliasName = aliasName.ToLower();
            a.aliasCommand = aliasCommand;
            aliasList.Add(a);
        }

        public void DeleteAlias(string aliasName)
        {
            alias temp = new alias();
            foreach (alias a in aliasList)
            {
                if (a.aliasName == aliasName)
                {
                    temp = a;
                }
            }
            if (temp.aliasName != "")
                aliasList.Remove(temp);
        }

        public void UpdateAlias(string aliasName, string aliasCommand)
        {
            // Horrible, kludgy replace ... need to rewrite when brain is working better

            alias search = new alias();
            alias temp = new alias();
            temp.aliasName = aliasName.ToLower();
            temp.aliasCommand = aliasCommand;

            foreach (alias a in aliasList)
            {
                if (a.aliasName.ToLower() == aliasName.ToLower())
                    search = a;
            }
            if (aliasList.IndexOf(search) > -1)
            {
                aliasList[aliasList.IndexOf(search)] = temp;
            }
        }

        public string GetAliasCommand(string aliasName)
        {
            string ret = "";
            foreach (alias a in aliasList)
            {
                if (a.aliasName.ToLower() == aliasName.ToLower())
                    ret = a.aliasCommand;
            }
            return ret;
        }

        public bool IsAlias(string aliasName)
        {
            bool ret = false;
            foreach (alias a in aliasList)
            {
                if (a.aliasName.ToLower() == aliasName.ToLower())
                    ret = true;
            }
            return ret;
        }

        #endregion

        #endregion
    }
}
