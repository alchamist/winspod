using System;

namespace MudServer.Api
{
    public class StatusDto
    {
        public string TalkerName { get; set; }
        public long UptimeSeconds { get; set; }
        public int ResidentCount { get; set; }
        public int PlayersConnectedTotal { get; set; }
        public int PlayersConnectedToday { get; set; }
        public double PlayersPerHour { get; set; }
        public DateTime ServerLocalTime { get; set; }
        public string ConnectAddress { get; set; }
        public int ConnectPort { get; set; }
        public string ContactEmail { get; set; }
    }

    public class OnlinePlayerDto
    {
        public string Username { get; set; }
        public string Rank { get; set; }
        public string Title { get; set; }
        public DateTime OnlineSince { get; set; }
        public long IdleSeconds { get; set; }
    }

    public class LeaderboardEntryDto
    {
        public int Rank { get; set; }
        public string Username { get; set; }
        public long TotalOnlineSeconds { get; set; }
    }

    public class FavouriteDto
    {
        public string Type { get; set; }
        public string Value { get; set; }
    }

    public class PlayerProfileDto
    {
        public string Username { get; set; }
        public string Rank { get; set; }
        public string Title { get; set; }
        public string Tagline { get; set; }
        public bool Online { get; set; }
        public DateTime? CurrentLogon { get; set; }
        public DateTime LastSeen { get; set; }
        public long LongestLoginSeconds { get; set; }
        public int PreviousLogins { get; set; }
        public TimeSpan AverageLoginTime { get; set; }
        public long TotalOnlineSeconds { get; set; }
        public DateTime ResidentSince { get; set; }
        public string ResidentBy { get; set; }
        public string Gender { get; set; }
        public bool BlockingShouts { get; set; }
        public int? ResCount { get; set; }
        public string OnChannels { get; set; }
        public string InformTag { get; set; }
        public string MaritalStatus { get; set; }
        public string Spouse { get; set; }
        public string RealName { get; set; }
        public string Occupation { get; set; }
        public string Hometown { get; set; }
        public string Email { get; set; }
        public string Jabber { get; set; }
        public string Icq { get; set; }
        public string Msn { get; set; }
        public string Yahoo { get; set; }
        public string Skype { get; set; }
        public string HomeUrl { get; set; }
        public string WorkUrl { get; set; }
        public string FacebookPage { get; set; }
        public string Twitter { get; set; }
        public int? SpodlistRank { get; set; }
        public int? SpodlistOutOf { get; set; }
        public FavouriteDto[] Favourites { get; set; }
    }
}
