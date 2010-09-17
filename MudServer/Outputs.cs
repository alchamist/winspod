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

        #region Output stuff

        public string centerText(string text)
        {
            return text.PadLeft((40 + (text.Length / 2)), ' ');
        }

        public static string rankName(int rank)
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
                        sendToUser("\r" + (c.myPlayer.InEditor ? "> " : c.myPlayer.Prompt.Replace("%t", DateTime.Now.ToShortTimeString()).Replace("%d", DateTime.Now.ToShortDateString())), c.myPlayer.UserName, false, c.myPlayer.DoColour, false, false);
                    }
                }
            }
        }

        private string[] matchPartial(string name)
        {
            //string source = @"players/";
            string source = Path.Combine(Server.userFilePath, "players");
            List<string> pNames = new List<string>();

            if (pNames.Count == 0)
            {
                if (Directory.Exists(source))
                {
                    string[] dirs = Directory.GetDirectories(source);

                    //foreach (string subdir in dirs)
                    //{
                        string[] fNames = Directory.GetFiles(@source);
                        foreach (string n in fNames)
                        {
                            string fn = Path.GetFileNameWithoutExtension(n);
                            if (fn.StartsWith(name, StringComparison.CurrentCultureIgnoreCase))
                            {
                                Player p = Player.LoadPlayer(fn, 0);
                                if (p != null)
                                    pNames.Add(p.UserName);
                            }
                        }
                    //}
                }
            }

            foreach (Connection c in connections)
            {
                if (c.socket.Connected && c.myState > 4 && c.myPlayer != null && pNames.IndexOf(c.myPlayer.UserName) < 0 && c.myPlayer.UserName.ToLower().StartsWith(name.ToLower()))
                    pNames.Add(c.myPlayer.UserName);
            }

            return pNames.ToArray();
        }

        public static bool isOnline(string username)
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
            string endChar = text.Substring(text.Length - 1);
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

        public static string formatTime(TimeSpan time)
        {
            string ret = "";
            if (time.TotalSeconds == 0)
                ret = "0 seconds";
            else
            {
                if (time.Days > 365)
                    ret = ((int)(time.Days / 365.25)).ToString() + " year" + (Math.Floor((double)time.Days / 365) > 1 ? "s" : "") + ", ";
                if (time.Days > 0)
                    ret += ((int)time.Days % 365.25).ToString() + " day" + (time.Days > 1 ? "s" : "") + ", ";
                if (time.Hours > 0 || time.Days > 0)
                    ret += time.Hours.ToString() + " hour" + (time.Hours > 1 ? "s" : "") + ", ";
                if (time.Minutes > 0 || time.Days > 0 || time.Hours > 0)
                    ret += time.Minutes.ToString() + " minute" + (time.Minutes > 0 ? "s " : " ");
                if (time.Seconds > 0)
                {
                    if (ret != "")
                        ret += "and ";
                    ret += time.Seconds.ToString() + " second" + (time.Seconds > 1 ? "s" : "");
                }
            }
            return ret;
        }

        public static string formatTimeNoZeros(TimeSpan ts)
        {
            string output = "";
            if (ts.Days > 0)
            {
                if (ts.Days > 365)
                    output += ((int)(ts.Days / 365)).ToString() + " year" + ((((int)(ts.Days / 365)) > 1) ? "s" : "") + ", " + (ts.Days - (((int)(ts.Days / 365)) * 365)).ToString() + " days, ";
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
