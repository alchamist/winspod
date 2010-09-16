using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.Reflection;

namespace MudServer
{
    class webserver
    {
        public static HttpListener httpserver = null;
        public static string _baseFolder = "webserver";

        string header = "";
        string footer = "";

        public webserver()
        {
            httpserver = new HttpListener();
            httpserver.Prefixes.Add("http://+:" + AppSettings.Default.HTTPPort.ToString() + "/");
            httpserver.Start();
            Console.WriteLine("HTTP Server alive and listening on port " + AppSettings.Default.HTTPPort.ToString());

            header = "<html>";
            header += "\r\n  <head>";
            header += "\r\n    <title>" + AppSettings.Default.TalkerName + " - Powered by Winspod II v " + Assembly.GetExecutingAssembly().GetName().Version.Major.ToString() + "</title>";
            header += "\r\n    <link rel='stylesheet' href='style.css' type='text/css'/>";
            header += "\r\n  </head>";
            header += "\r\n  <body>";
            header += "\r\n    <div style='margin-top: 5px; margin-bottom: 20px; width: 765px; margin-left: auto; margin-right: auto; background-color: #ffffff; border: solid 1px #000000; padding: 2px 0px 2px 0px; text-align: center;'>";
            header += "\r\n    <h1 class='title'>" + AppSettings.Default.TalkerName + "</h1>";
            header += "\r\n    </div>";
            header += "\r\n\r\n";


            footer = "\r\n\r\n  <br><br><hr><center>Powered by <a href = 'http://code.google.com/p/winspod/'>Winspod II</a> - Copyright (C) 2001 - " + DateTime.Now.Year.ToString() + " Jay Eames</center>";
            footer += "\r\n  </body>";
            footer += "\r\n</html>";

            while (true)
                try
                {
                    HttpListenerContext request = httpserver.GetContext();
                    ThreadPool.QueueUserWorkItem(ProcessRequest, request);
                }
                catch (HttpListenerException) { break; }
                catch (InvalidOperationException) { break; }
        }

        void ProcessRequest(object listenerContext)
        {
            try
            {
                var context = (HttpListenerContext)listenerContext;
                string filename = (Path.GetFileName(context.Request.RawUrl) == "" ? "index.html" : Path.GetFileName(context.Request.RawUrl));
                string path = Path.Combine(_baseFolder, filename);
                byte[] msg = null;
                if (!File.Exists(path))
                {
                    Player target = null;
                    if (context.Request.Url.Segments.Length == 3 && context.Request.Url.Segments[1].ToLower() == "player/")
                        target = Player.LoadPlayer(filename.Replace("player/", ""),0);

                    if (target != null && target.UserName != null)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        msg = Encoding.UTF8.GetBytes(header + "<center><h2>Player Info: " + target.UserName + "</h2><br><div style='width:650px; background: black; color: white; padding: 10px;'><div style='text-align: left;font-family: Courier;'>" + GetPlayerInfo(target) + "</div></div>" + footer);
                    }
                    else
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        msg = Encoding.UTF8.GetBytes(header + "<center><h1>Sorry, that page does not exist</h1></center>" + footer);
                    }
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    //msg = Encoding.UTF8.GetBytes(header) & File.ReadAllBytes(path) & Encoding.UTF8.GetBytes(footer);
                    string file = File.ReadAllText(path);

                    file = file.Replace("{ONLINE_LIST}", GetOnlineList());

                    if (Path.GetExtension(path) != ".css")
                        msg = Encoding.UTF8.GetBytes(header + file + footer);
                    else
                        msg = Encoding.UTF8.GetBytes(file);
                    
                }
                context.Response.ContentLength64 = msg.Length;
                using (Stream s = context.Response.OutputStream)
                    s.Write(msg, 0, msg.Length);
            }
            catch (Exception ex) { Console.WriteLine("Request error: " + ex); }
        }

        private string GetOnlineList()
        {
            string ret = "";
            int playerCount = 0;
            foreach (Connection c in Connection.connections)
            {
                if (c.socket.Connected && c.myPlayer != null && c.myState > 4 && c.myPlayer.UserName != null && (!c.myPlayer.Invisible && c.myPlayer.PlayerRank >= (int)Player.Rank.Admin))
                {
                    playerCount++;
                    if (ret == "")
                        ret = "<table cellpadding=5 cellspacing=0 class='onlinelist'>\r\n<tr class='onlinelist_header'><td>Player</td><td>Rank</td><td>Time online</td><td>Idle</td></tr>";

                    ret += "\r\n<tr class='rank" + c.myPlayer.PlayerRank.ToString() + "'>";
                    ret += "\r\n  <td><a href='player/" + c.myPlayer.UserName.ToLower() + "'>" + c.myPlayer.UserName + "</a> " + c.myPlayer.Title + "</td>";
                    ret += "\r\n  <td>" + Connection.rankName(c.myPlayer.PlayerRank) + "</td>";
                    ret += "\r\n  <td>" + Connection.formatTime(DateTime.Now - c.myPlayer.CurrentLogon) + "</td>";
                    ret += "\r\n  <td>" + Connection.formatTime(DateTime.Now - c.myPlayer.LastActive) + "</td>";
                    ret += "\r\n</tr>";
                }
            }
            ret = (ret == "" ? "<table class='onlinelist'><tr><td>There are no players currently online</td></tr></table>" : ret + "\r\n<tr><td colspan=5><center>There " + (playerCount == 1 ? "is " :  "are ") + playerCount.ToString() + " user" + (playerCount==1 ? "" : "s") + " online</td></tr>\r\n</table>");
            return AnsiColour.Colorise(ret, true);
        }

        private string GetPlayerInfo(Player ex)
        {
            string output = "Player not found";
            if (ex != null && ex.PlayerRank > (int)Player.Rank.Newbie)
            {
                string line = "<span style='color: cyan;'>" + "".PadRight(80, '-') + "</span>";
                bool online = Connection.isOnline(ex.UserName);
                output = ("<span style='color: cyan'>---[</span>" + ex.UserName + "<span style='color: cyan;'>]").PadRight(140, '-').Replace(ex.UserName, ex.ColourUserName) + "</span><br>";
                //output = output.PadRight(104, '-') + "{reset}<br>";
                output += (ex.Prefix + " " + "<span class='rank" + ex.PlayerRank + "'>" +  ex.UserName + "</span> " + ex.Title).Trim() + "<br>";
                output += "{bold}{cyan}" + line + "{reset}<br>";
                if (ex.Tagline != "")
                {
                    output += ex.Tagline + "<br>{bold}{cyan}" + line + "{reset}<br>";
                }

                output += "<span style='color: blue;'>";
                if (online && !ex.Invisible)
                {
                    output += "{bold}{blue}Online since {reset}".PadRight(48, ' ') + ": {blue}" + ex.CurrentLogon.ToString() + "{reset}<br>";
                }
                else
                {
                    output += "{bold}{blue}Last seen {reset}".PadRight(48, ' ') + ": {blue}" + ex.LastLogon.ToString() + "{reset}<br>";
                }
                if (online && !ex.Invisible)
                {
                    string time = (DateTime.Now - ex.CurrentLogon).ToString();
                    output += "{bold}{blue}Time Online {reset}".PadRight(48, ' ') + ": {blue}" + time.Remove(time.IndexOf('.')) + "{reset}<br>";
                }
                string longest;
                if (online && ((DateTime.Now - ex.CurrentLogon).TotalSeconds > ex.LongestLogin && !ex.Invisible))
                    longest = (DateTime.Now - ex.CurrentLogon).ToString();
                else
                    longest = (DateTime.Now.AddSeconds((double)ex.LongestLogin) - DateTime.Now).ToString();

                output += "{bold}{blue}Longest Login {reset}".PadRight(48, ' ') + ": {blue}" + (longest.IndexOf('.') > -1 ? longest.Remove(longest.IndexOf('.')) : longest) + "{reset}<br>";
                output += "{bold}{blue}Previous Logins {reset}".PadRight(48, ' ') + ": {blue}" + ex.LoginCount.ToString() + "{reset}<br>";
                output += "{bold}{blue}Average Logon Time {reset}".PadRight(48, ' ') + ": {blue}" + ex.AverageLoginTime.ToString() + "{reset}<br>";

                //string tOnline = TimeSpan.FromSeconds((DateTime.Now - ex.CurrentLogon).TotalSeconds + ex.TotalOnlineTime).Days.ToString();
                //output += "{bold}{blue}Total Online Time {reset}".PadRight(48, ' ') + ": {blue}" + tOnline.Remove(tOnline.IndexOf('.')) + "{reset}<br>";
                string tOnline = (online ? Connection.formatTimeNoZeros(TimeSpan.FromSeconds((DateTime.Now - ex.CurrentLogon).TotalSeconds + ex.TotalOnlineTime)) : Connection.formatTimeNoZeros(TimeSpan.FromSeconds(ex.TotalOnlineTime)));
                output += "{bold}{blue}Total Online Time {reset}".PadRight(48, ' ') + ": {blue}" + tOnline + "{reset}<br>";

                if (ex.PlayerRank > (int)Player.Rank.Newbie)
                {
                    output += "{bold}{blue}Resident Since {reset}".PadRight(48, ' ') + ": {blue}" + ex.ResDate.ToString() + "{reset}<br>";
                    TimeSpan rAge = TimeSpan.FromSeconds((DateTime.Now - ex.ResDate).TotalSeconds);
                    int rAgeYears = (int)Math.Floor(rAge.TotalDays / 365);
                    int rAgeDays = (int)rAge.TotalDays % 365;
                    output += "{bold}{blue}Resident age {reset}".PadRight(48, ' ') + ": {blue}" + (rAgeYears > 0 ? (rAgeYears.ToString() + " Year, " + (rAgeYears > 1 ? "s" : "")) : "") + rAgeDays.ToString() + " day" + (rAgeDays == 1 ? "" : "s") + " {reset}<br>";
                    output += "{bold}{blue}Ressed by {reset}".PadRight(48, ' ') + ": {blue}" + ex.ResBy + "{reset}<br>";
                }

                output += "</span><span style='color: magenta;'>";

                if (ex.EmailAddress != "" && ex.EmailPermissions == (int)Player.ShowTo.Public)
                    output += "{bold}{magenta}E-mail Address {reset}".PadRight(51, ' ') + ": {magenta}" + ex.EmailAddress + "{reset}<br>";
                if (ex.JabberAddress != "")
                    output += "{bold}{magenta}Jabber {reset}".PadRight(51, ' ') + ": {magenta}" + ex.JabberAddress + "{reset}<br>";
                if (ex.ICQAddress != "")
                    output += "{bold}{magenta}ICQ {reset}".PadRight(51, ' ') + ": {magenta}" + ex.ICQAddress + "{reset}<br>";
                if (ex.MSNAddress != "")
                    output += "{bold}{magenta}MSN {reset}".PadRight(51, ' ') + ": {magenta}" + ex.MSNAddress + "{reset}<br>";
                if (ex.YahooAddress != "")
                    output += "{bold}{magenta}Yahoo {reset}".PadRight(51, ' ') + ": {magenta}" + ex.YahooAddress + "{reset}<br>";
                if (ex.SkypeAddress != "")
                    output += "{bold}{magenta}Skype {reset}".PadRight(51, ' ') + ": {magenta}" + ex.SkypeAddress + "{reset}<br>";
                if (ex.HomeURL != "")
                    output += "{bold}{magenta}Home URL {reset}".PadRight(51, ' ') + ": {magenta}" + ex.HomeURL + "{reset}<br>";
                if (ex.WorkURL != "")
                    output += "{bold}{magenta}Work URL {reset}".PadRight(51, ' ') + ": {magenta}" + ex.WorkURL + "{reset}<br>";
                if (ex.FacebookPage != "")
                    output += "{bold}{magenta}Facebook Page {reset}".PadRight(51, ' ') + ": {magenta}" + ex.FacebookPage + "{reset}<br>";
                if (ex.Twitter != "")
                    output += "{bold}{magenta}Twitter ID {reset}".PadRight(51, ' ') + ": {magenta}" + ex.Twitter + "{reset}<br>";

                for (int i = 0; i < ex.favourites.Count; i++)
                {
                    if (ex.favourites[i].value != "" && ex.favourites[i].type != "")
                        output += ("{bold}{magenta}Favourite " + ex.favourites[i].type + " {reset}").PadRight(51, ' ') + ": {magenta}" + ex.favourites[i].value + "{reset}<br>";
                }

                output += "</span><span style='color: cyan;'>";

                if (ex.RealName != "")
                    output += "{bold}{cyan}IRL Name {reset}".PadRight(48, ' ') + ": {cyan}" + ex.RealName + "{reset}<br>";
                if (ex.DateOfBirth != DateTime.MinValue)
                    output += "{bold}{cyan}Age {reset}".PadRight(48, ' ') + ": {cyan}" + ((int)((DateTime.Now.Subtract(ex.DateOfBirth)).Days / 365.25)).ToString() + "{reset}<br>";
                if (ex.Occupation != "")
                    output += "{bold}{cyan}Occupation {reset}".PadRight(48, ' ') + ": {cyan}" + ex.Occupation + "{reset}<br>";
                if (ex.Hometown != "")
                    output += "{bold}{cyan}Home Town {reset}".PadRight(48, ' ') + ": {cyan}" + ex.Hometown + "{reset}<br>";

                output += "{bold}{cyan}Local Time {reset}".PadRight(48, ' ') + ": {cyan}" + DateTime.Now.AddHours(ex.JetLag).ToShortTimeString() + "{reset}<br>";

                output += "</span><span style='color: yellow;'>";

                output += "{bold}{yellow}Gender {reset}".PadRight(50, ' ') + ": {yellow}" + (Connection.gender)ex.Gender + "{reset}<br>";
                output += "{bold}{yellow}Rank {reset}".PadRight(50, ' ') + ": " + Connection.rankName(ex.PlayerRank) + "{reset}<br>";
                output += "{bold}{yellow}Blocking Shouts {reset}".PadRight(50, ' ') + ": {yellow}" + (ex.HearShouts ? "No" : "Yes") + "{reset}<br>";

                if (ex.PlayerRank >= (int)Player.Rank.Staff)
                    output += "{bold}{yellow}Has ressed{reset}".PadRight(50, ' ') + ": {yellow}" + ex.ResCount.ToString() + " player" + (ex.ResCount == 1 ? "" : "s") + "{reset}<br>";

                Player.privs pPrivs = ex.SpecialPrivs;
                if (pPrivs.builder || pPrivs.tester)
                {
                    output += "{bold}{yellow}Special Privs {reset}".PadRight(50, ' ') + ": {yellow}";
                    if (pPrivs.builder) output += "[builder] ";
                    if (pPrivs.tester) output += "[tester] ";
                    if (pPrivs.noidle) output += "[noidle] ";
                    if (pPrivs.spod) output += "[spod] ";
                    if (pPrivs.minister) output += "[minster] ";
                    output = output.Remove(output.Length - 1, 1) + "{reset}<br>";
                }

                output += "{bold}{yellow}On Channels {reset}".PadRight(50, ' ') + ": {yellow}" + Connection.getChannels(ex.UserName) + "{reset}<br>";
                if (ex.InformTag != "")
                    output += "{bold}{yellow}Inform Tag {reset}".PadRight(50, ' ') + ": {yellow}[" + ex.InformTag + "{reset}{yellow}]{reset}<br>";

                output += "{bold}{yellow}Marital Status {reset}".PadRight(50, ' ') + ": {yellow}";
                if (ex.maritalStatus > Player.MaritalStatus.ProposedTo && ex.Spouse != "")
                    output += ex.maritalStatus.ToString() + (ex.maritalStatus == Player.MaritalStatus.Engaged || ex.maritalStatus == Player.MaritalStatus.Married ? " to " : (ex.maritalStatus == Player.MaritalStatus.Divorced ? " from " : " by ")) + ex.Spouse;
                else
                    output += "Single";
                output += "{reset}<br>";
                output += "</span>";
                output += "{bold}{cyan}" + line + "{reset}";
            }
            return AnsiColour.Colorise(output, true).Replace(" ","&nbsp;").Replace("span&nbsp;", "span ").Replace("color:&nbsp;", "color:");
        }
    }
}
