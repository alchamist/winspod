using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MudServer
{
    public partial class Connection
    {
        public void cmdRank(string message)
        {
            int num = 0;
            List<Player> playerList = getPlayers();
            string output = "";

            playerList.Sort(delegate(Player p1, Player p2) { return p2.TotalOnlineTime.CompareTo(p1.TotalOnlineTime); });

            if (message == "")
            {
                // Looking for own rank
                for (int i = 0; i < playerList.Count; i++)
                {
                    if (playerList[i].UserName == myPlayer.UserName)
                    {
                        output += myPlayer.UserName + " ^Gis ranked^N " + (i + 1).ToString() + " ^G(out of " + playerList.Count + ") in the &t spods list^N\r\n" + footerLine();
                        // We've found our player, now to calculate 3 above and 3 below ...
                        int start = 0;
                        int end = 0;
                        if (i - 4 > 0)
                        {
                            start = i - 4;
                        }
                        for (int j = start; j < i; j++)
                        {
                            output += "\r\n^C" + (j + 1).ToString().PadRight(7) + "^N" + playerList[j].UserName + " ^Gwith^N " + formatTime(TimeSpan.FromSeconds(playerList[j].TotalOnlineTime));
                        }
                        output += "\r\n^C" + (i + 1).ToString().PadRight(7) + "^N" + playerList[i].UserName + " ^Gwith^N " + formatTime(TimeSpan.FromSeconds(playerList[i].TotalOnlineTime));

                        if (i+1 != playerList.Count)
                        {
                            if (i + 4 >= playerList.Count)
                                end = playerList.Count - 1;
                            else
                                end = i + 4;

                            start = i + 1;
                            for (int j = start; j <= end; j++)
                            {
                                output += "\r\n^C" + (j + 1).ToString().PadRight(7) + "^N" + playerList[j].UserName + " ^Gwith^N " + formatTime(TimeSpan.FromSeconds(playerList[j].TotalOnlineTime));
                            }
                        }
                    }
                }

            }
            else if (int.TryParse(message, out num))
            {
                // Looking for a rank number
                if (num > playerList.Count)
                {
                    sendToUser("No such rank", true, false, false);
                    return;
                }
                else
                {
                    for (int i = 0; i < playerList.Count; i++)
                    {
                        if (i+1 == num)
                        {
                            // We've found our player, now to calculate 3 above and 3 below ...
                            int realStart = 0;
                            int realEnd = 0;
                            int start = 0;
                            int end = 0;
                            if (i - 4 > 0)
                            {
                                start = i - 4;
                            }
                            realStart = start + 1;
                            for (int j = start; j < i; j++)
                            {
                                output += "\r\n^C" + (j + 1).ToString().PadRight(7) + "^N" + playerList[j].UserName + " ^Gwith^N " + formatTime(TimeSpan.FromSeconds(playerList[j].TotalOnlineTime));
                            }
                            output += "\r\n^C" + (i + 1).ToString().PadRight(7) + "^N" + playerList[i].UserName + " ^Gwith^N " + formatTime(TimeSpan.FromSeconds(playerList[i].TotalOnlineTime));

                            if (i + 1 != playerList.Count)
                            {
                                if (i + 4 >= playerList.Count)
                                    end = playerList.Count - 1;
                                else
                                    end = i + 4;

                                start = i + 1;
                                for (int j = start; j <= end; j++)
                                {
                                    output += "\r\n^C" + (j + 1).ToString().PadRight(7) + "^N" + playerList[j].UserName + " ^Gwith^N " + formatTime(TimeSpan.FromSeconds(playerList[j].TotalOnlineTime));
                                }
                            }
                            realEnd = (end == 0 ? playerList.Count : end+1);
                            output = "^GPositions ^N" + realStart.ToString() + " ^Gto^N " + realEnd.ToString() + " ^G(out of " + playerList.Count.ToString() + ") in the &t spods list\r\n" + footerLine() + output;
                        }
                    }

                }
            }
            else
            {
                // Looking for a player
                string[] target = matchPartial(message);
                if (target.Length == 0)
                {
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                    return;
                }
                else if (target.Length > 1)
                {
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                    return;
                }
                else
                {
                    for (int i = 0; i < playerList.Count; i++)
                    {
                        if (playerList[i].UserName.ToLower() == target[0].ToLower())
                        {
                            output += playerList[i].UserName + " ^Gis ranked^N " + (i + 1).ToString() + " ^G(out of " + playerList.Count + ") in the &t spods list^N\r\n" + footerLine();
                            // We've found our player, now to calculate 3 above and 3 below ...
                            int start = 0;
                            int end = 0;
                            if (i - 4 > 0)
                            {
                                start = i - 4;
                            }
                            for (int j = start; j < i; j++)
                            {
                                output += "\r\n^C" + (j + 1).ToString().PadRight(7) + "^N" + playerList[j].UserName + " ^Gwith^N " + formatTime(TimeSpan.FromSeconds(playerList[j].TotalOnlineTime));
                            }
                            output += "\r\n^C" + (i + 1).ToString().PadRight(7) + "^N" + playerList[i].UserName + " ^Gwith^N " + formatTime(TimeSpan.FromSeconds(playerList[i].TotalOnlineTime));

                            if (i + 1 != playerList.Count)
                            {
                                if (i + 4 >= playerList.Count)
                                    end = playerList.Count - 1;
                                else
                                    end = i + 4;

                                start = i + 1;
                                for (int j = start; j <= end; j++)
                                {
                                    output += "\r\n^C" + (j + 1).ToString().PadRight(7) + "^N" + playerList[j].UserName + " ^Gwith^N " + formatTime(TimeSpan.FromSeconds(playerList[j].TotalOnlineTime));
                                }
                            }
                        }
                    }
                }
            }
            sendToUser(headerLine("Rank: " + message) + "\r\n" + output + "\r\n" + footerLine(), true, false, false);
        }
    }
}
