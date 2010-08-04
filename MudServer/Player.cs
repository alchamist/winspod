﻿using System;
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

        public struct privs
        {
            public bool builder;
            public bool tester;
            public bool noidle;
        }

        public struct friend
        {
            static string friendName;
            static bool friendNotify;
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

        #endregion

        #region user RL details

        private string      realName = "";                                  // Their real name
        private string      description = "is a newbie, be nice";           // Their description
        private string      email = "";                                     // Their e-mail address
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
        private int         slapped;                                        // Number of times a player has been slapped
        private int         logons;                                         // Number of times they have logged on

        #endregion

        private friend[]    friends = new friend[50];                       // Friends list

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

        public string Description
        {
            get { return description; }
            set { description = value; }
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

        public bool SeeEcho
        {
            get { return seeEchoFrom; }
            set { seeEchoFrom = value; }
        }

        #endregion

        #region Constructor/Destructor

        public Player()
        {
            //username = "Wibble" + userConn.ToString();
        }

        ~Player()
        {
            if (rank!=Rank.Newbie)
                SavePlayer();
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

        #endregion
    }
}