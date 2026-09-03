using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MudServer.Api
{
    /// <summary>
    /// Replaces the old hand-rolled HttpListener server (webserver/webserver.cs), which
    /// parsed raw HTTP requests, built HTML by string concatenation, and re-derived its
    /// own routing and content-type handling.
    ///
    /// This is a plain JSON API hosted on Kestrel via ASP.NET Core's minimal APIs.
    /// The server no longer owns "the website" - it just publishes game state as data.
    /// Any front end (a static site, a SPA, a mobile app, curl) can build the actual
    /// web presence against this without re-implementing HTTP parsing, and the API can
    /// be split into its own process/deployment later without touching the game loop.
    /// </summary>
    public static class MudApiEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapGet("/api/status", GetStatus);
            app.MapGet("/api/who", GetWho);
            app.MapGet("/api/leaderboard", GetLeaderboard);
            app.MapGet("/api/players/{username}", GetPlayer);
        }

        static StatusDto GetStatus()
        {
            List<Player> playerList = LoadAllPlayers();
            TimeSpan uptime = DateTime.Now - Server.startTime;
            double perHour = uptime.TotalHours > 0 ? Server.playerCount / uptime.TotalHours : 0;

            return new StatusDto
            {
                TalkerName = AppSettings.Default.TalkerName,
                UptimeSeconds = (long)uptime.TotalSeconds,
                ResidentCount = playerList.Count,
                PlayersConnectedTotal = Server.playerCount,
                PlayersConnectedToday = Server.playerCountToday,
                PlayersPerHour = Math.Round(perHour, 2),
                ServerLocalTime = DateTime.Now,
                ConnectAddress = AppSettings.Default.TalkerAddress,
                ConnectPort = AppSettings.Default.Port,
                ContactEmail = AppSettings.Default.TalkerEmail
            };
        }

        static IResult GetWho()
        {
            // Same visibility rule the old GetOnlineList() html used.
            var online = Connection.connections
                .Cast<Connection>()
                .Where(c => c.socket.Connected && c.myPlayer != null && c.myState > 4
                    && c.myPlayer.UserName != null
                    && !c.myPlayer.Invisible && c.myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                .Select(c => new OnlinePlayerDto
                {
                    Username = c.myPlayer.UserName,
                    Rank = Connection.rankName(c.myPlayer.PlayerRank),
                    Title = c.myPlayer.Title,
                    OnlineSince = c.myPlayer.CurrentLogon,
                    IdleSeconds = (long)(DateTime.Now - c.myPlayer.LastActive).TotalSeconds
                })
                .ToList();

            return Results.Ok(online);
        }

        static LeaderboardEntryDto[] GetLeaderboard()
        {
            List<Player> playerList = LoadAllPlayers();
            playerList.Sort((p1, p2) => p2.TotalOnlineTime.CompareTo(p1.TotalOnlineTime));

            return playerList
                .Take(20)
                .Select((p, i) => new LeaderboardEntryDto
                {
                    Rank = i + 1,
                    Username = p.UserName,
                    TotalOnlineSeconds = p.TotalOnlineTime
                })
                .ToArray();
        }

        static IResult GetPlayer(string username)
        {
            Player ex = Player.LoadPlayer(username, 0);

            if (ex == null || ex.UserName == null || ex.PlayerRank <= (int)Player.Rank.Newbie)
                return Results.NotFound();

            bool online = Connection.isOnline(ex.UserName);
            bool showOnlineDetail = online && !ex.Invisible;

            var dto = new PlayerProfileDto
            {
                Username = ex.UserName,
                Rank = Connection.rankName(ex.PlayerRank),
                Title = ex.Title,
                Tagline = ex.Tagline,
                Online = showOnlineDetail,
                CurrentLogon = showOnlineDetail ? ex.CurrentLogon : (DateTime?)null,
                LastSeen = ex.LastLogon,
                LongestLoginSeconds = ex.LongestLogin,
                PreviousLogins = ex.LoginCount,
                AverageLoginTime = ex.AverageLoginTime,
                TotalOnlineSeconds = showOnlineDetail
                    ? (long)((DateTime.Now - ex.CurrentLogon).TotalSeconds + ex.TotalOnlineTime)
                    : ex.TotalOnlineTime,
                ResidentSince = ex.ResDate,
                ResidentBy = ex.ResBy,
                Gender = ((Connection.gender)ex.Gender).ToString(),
                BlockingShouts = !ex.HearShouts,
                ResCount = ex.PlayerRank >= (int)Player.Rank.Staff ? ex.ResCount : (int?)null,
                OnChannels = Connection.getChannels(ex.UserName),
                InformTag = ex.InformTag,
                MaritalStatus = ex.maritalStatus > Player.MaritalStatus.ProposedTo && ex.Spouse != ""
                    ? ex.maritalStatus.ToString()
                    : "Single",
                Spouse = ex.maritalStatus > Player.MaritalStatus.ProposedTo ? ex.Spouse : null,
                RealName = ex.RealName,
                Occupation = ex.Occupation,
                Hometown = ex.Hometown,
                Email = ex.EmailPermissions == (int)Player.ShowTo.Public ? ex.EmailAddress : null,
                Jabber = NullIfEmpty(ex.JabberAddress),
                Icq = NullIfEmpty(ex.ICQAddress),
                Msn = NullIfEmpty(ex.MSNAddress),
                Yahoo = NullIfEmpty(ex.YahooAddress),
                Skype = NullIfEmpty(ex.SkypeAddress),
                HomeUrl = NullIfEmpty(ex.HomeURL),
                WorkUrl = NullIfEmpty(ex.WorkURL),
                FacebookPage = NullIfEmpty(ex.FacebookPage),
                Twitter = NullIfEmpty(ex.Twitter),
                Favourites = ex.favourites
                    .Where(f => f.value != "" && f.type != "")
                    .Select(f => new FavouriteDto { Type = f.type, Value = f.value })
                    .ToArray()
            };

            int[] rank = Connection.getRank(ex.UserName);
            if (rank[0] > -1)
            {
                dto.SpodlistRank = rank[0];
                dto.SpodlistOutOf = rank[1];
            }

            return Results.Ok(dto);
        }

        static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        static List<Player> LoadAllPlayers()
        {
            var list = new List<Player>();
            string path = Path.Combine(Server.userFilePath, "players" + Path.DirectorySeparatorChar);
            if (!Directory.Exists(path))
                return list;

            foreach (FileInfo file in new DirectoryInfo(path).GetFiles())
            {
                Player load = Player.LoadPlayer(file.Name.Replace(".xml", ""), 0);
                if (load != null)
                    list.Add(load);
            }
            return list;
        }
    }
}
