using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace MudServer
{
    public partial class Connection
    {
        private int[,] msGrid;
        private bool[,] msShow;
        private bool msInGame = false;

        public void cmdMSweep(string message)
        {
            if (message.ToLower() == "new")
            {
                msGrid = new int[8, 8];
                msShow = new bool[8, 8];
                // Set inital values to -1 to indicate covered square
                for (int i = 0; i < 8; i++)
                {
                    for (int j = 0; j < 8; j++)
                    {
                        msGrid[i, j] = 0;
                        msShow[i, j] = false;
                    }
                }

                // Put some bombs in ... 
                Random r = new Random();
                int x;
                int y;
                int count = 0;
                while (count < 10)
                {
                    x = r.Next(0, 8);
                    y = r.Next(0, 8);
                    if (msGrid[x, y] != 9)
                    {
                        msGrid[x, y] = 9;
                        count++;
                        for (int i = x - 1; i < x + 2; i++)
                        {
                            for (int j = y - 1; j < y + 2; j++)
                            {
                                try
                                {
                                    if (i >= 0 && i < 8 && j >= 0 && j < 8)
                                    {
                                        if (msGrid[i, j] != 9)
                                            msGrid[i, j]++;
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                }

                msInGame = true;
                msShowGrid(false);

            }
            else if (message.IndexOf(' ') > 0 && msInGame)
            {
                string[] split = message.Split(new char[] { ' ' }, 2);
                int x;
                int y;
                if (int.TryParse(split[0], out y) && int.TryParse(split[1], out x))
                {
                    if (x > 0 && x < 9 && y > 0 && y < 9)
                    {
                        msCheckSquare(x-1, y-1);
                    }
                    else
                    {
                        sendToUser("Syntax: msweep <x> <y>", true, false, false);
                    }
                }
                else
                {
                    sendToUser("Syntax: msweep <x> <y>", true, false, false);
                }
            }
            else
            {
                if (!msInGame)
                {
                    sendToUser("You do not have an active Minesweeper game", true, false, false);
                }
                else
                {
                    msShowGrid();
                }
            }
            //sendToUser("Sorry, this command hasn't been implimented yet", true, false, false);
        }

        private void msShowGrid()
        {
            msShowGrid(false);
        }

        private void msShowGrid(bool reveal)
        {
            string output = "^R 1 2 3 4 5 6 7 8^N\r\n";
            for (int i = 0; i < 8; i++)
            {
                output += (i+1).ToString();
                for (int j = 0; j < 8; j++)
                {
                    if (reveal)
                    {
                        if (msGrid[i, j] == 9)
                            output += "^RB^N";
                        else if (msGrid[i, j] == 0)
                            output += " ";
                        else
                            output += msGrid[i, j].ToString();
                    }
                    else
                    {
                        if (msShow[i, j])
                        {
                            if (msGrid[i, j] == 9)
                                output += "B";
                            else if (msGrid[i, j] == 0)
                                output += " ";
                            else if (msGrid[i, j] == ' ')
                                output += " ";
                            else if (msGrid[i, j] == '1')
                                output += "^B1^N";
                            else if (msGrid[i, j] == '2')
                                output += "^G2^N";
                            else
                                output += "^R" + msGrid[i, j] + "^N";

                        }
                        else
                        {
                            output += "*";
                        }
                        
                    }
                    output += " ";
                }
                output = output.Trim() + "\r\n";
            }


            sendToUser(output, true, false, false);
        }

        private void msCheckSquare(int x, int y)
        {
            if (msShow[x, y])
            {
                sendToUser("You have already checked that square", true, false, false);
            }
            else if (msGrid[x, y] == 9)
            {
                // Bomb found - game over!
                msInGame = false;
                msShowGrid(true);
                sendToUser("^RKABOOOOOOOOOOM^N - Game Over", true, false, false);
                myPlayer.minesweeper.lost++;
            }
            else
            {
                // No bomb found!
                msShow[x, y] = true;
                int last = -1;
                int covered = msCountCovered();
                while (covered != last)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        for (int j = 0; j < 8; j++)
                        {
                            msClearSquare(i, j);
                        }
                    }
                    last = covered;
                    covered = msCountCovered();
                }
                covered = msCountCovered();
                if (covered == 10)
                {
                    // Won!
                    msShowGrid(true);
                    sendToUser("^YCongratulations - You win!^N", true, false, false);
                    myPlayer.minesweeper.won++;
                    msInGame = false;
                }
                else
                {
                    msShowGrid();
                    sendToUser("Phew - you survived that one!", true, false, false);
                }
            }
        }

        private int msCountCovered()
        {
            int ret = 0;

            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    if (!msShow[i,j])
                        ret++;
                }
            }
            return ret;
        }

        private void msClearSquare(int x, int y)
        {
            if (msShow[x, y] == true && msGrid[x, y] == 0)
            {
                for (int i = x - 1; i < x + 2; i++)
                {
                    for (int j = y - 1; j < y + 2; j++)
                    {
                        if (i >= 0 && i < 8 && j >= 0 && j < 8)
                        {
                            msShow[i, j] = true;
                        }
                    }
                }
            }
        }

        #region Slots

        // Weighted so the jackpot symbol (7) is rare - duplicated entries is a simple way
        // to weight a flat Random.Next pick without a separate cumulative-probability table.
        private static readonly string[] slotSymbols = { "BAR", "BAR", "BAR", "BEL", "BEL", "BEL", "CHY", "CHY", "CHY", "CHY", "LEM", "LEM", "LEM", "LEM", "7" };
        private static readonly string[] slotColours = { "^Y", "^Y", "^Y", "^G", "^G", "^G", "^R", "^R", "^R", "^R", "^P", "^P", "^P", "^P", "^C" };

        private const int slotsWager = 5;

        // Payout structure and flavour lifted from Playground+'s slots.c (talkers/pgplus) -
        // a fixed wager into a server-wide pot (Server.slotsPot) that grows on a loss and
        // pays out on a win: the whole pot for three 7s, half for three cherries, a quarter
        // for any other three-of-a-kind, and a plain refund for just two matching. Simplified
        // from pgplus's multi-line ASCII-art reels down to the single-line bracket display
        // the rest of this codebase uses, but the actual stakes/tiers are the real thing.
        public void cmdSlots(string message)
        {
            if (myPlayer.Credits < slotsWager)
            {
                sendToUser("Sorry, you don't have enough credits to play (you need at least " + slotsWager + ").", true, false, false);
                return;
            }

            myPlayer.Credits -= slotsWager;

            Random r = new Random();
            int[] pick = new int[3];
            string reels = "";
            for (int i = 0; i < 3; i++)
            {
                pick[i] = r.Next(slotSymbols.Length);
                reels += "[ " + slotColours[pick[i]] + slotSymbols[pick[i]].PadLeft(3) + "^N ] ";
            }
            string s0 = slotSymbols[pick[0]], s1 = slotSymbols[pick[1]], s2 = slotSymbols[pick[2]];

            sendToUser("You feed " + slotsWager + " credits into the machine and pull the lever...", true, false, false);
            sendToUser(reels.Trim(), true, false, false);

            if (s0 == s1 && s1 == s2)
            {
                int payout;
                string flavour;
                if (s0 == "7")
                {
                    payout = Server.slotsPot;
                    flavour = "^Y*** JACKPOT! The whole pot is yours! ***^N";
                }
                else if (s0 == "CHY")
                {
                    payout = Server.slotsPot / 2;
                    flavour = "^GCherries! Half the pot is yours!^N";
                }
                else
                {
                    payout = Server.slotsPot / 4;
                    flavour = "^GA match! A quarter of the pot is yours!^N";
                }
                Server.slotsPot -= payout;
                myPlayer.Credits += payout;
                sendToUser(flavour + " You win " + payout + " credit" + (payout == 1 ? "" : "s") + ".", true, false, false);
                myPlayer.slots.won++;
            }
            else if (s0 == s1 || s1 == s2 || s0 == s2)
            {
                myPlayer.Credits += slotsWager;
                sendToUser("Not too bad - you get your " + slotsWager + " credits back.", true, false, false);
            }
            else
            {
                Server.slotsPot += slotsWager;
                sendToUser("No match - better luck next time.", true, false, false);
                myPlayer.slots.lost++;
            }

            sendToUser("^NYour credits: " + myPlayer.Credits + " | Pot: " + Server.slotsPot, true, false, false);
            myPlayer.SavePlayer();
        }

        #endregion

        #region Blackjack

        // Rank only - suit doesn't affect blackjack value, so there's no point tracking it.
        // 1-9 are their face value, 10-13 (10/J/Q/K) are all worth 10, 1 is the ace.
        private List<int> bjPlayerHand;
        private List<int> bjDealerHand;
        private bool bjInGame = false;

        private int cardValue(int rank)
        {
            return Math.Min(rank, 10);
        }

        private int handValue(List<int> hand)
        {
            int total = 0;
            int aces = 0;
            foreach (int rank in hand)
            {
                total += cardValue(rank);
                if (rank == 1)
                    aces++;
            }
            // Aces start counted as 1 (cardValue's Math.Min already does that); upgrade one
            // at a time to 11 as long as it doesn't bust, same "soft ace" rule as the real
            // game.
            while (aces > 0 && total + 10 <= 21)
            {
                total += 10;
                aces--;
            }
            return total;
        }

        private string cardName(int rank)
        {
            switch (rank)
            {
                case 1: return "Ace";
                case 11: return "Jack";
                case 12: return "Queen";
                case 13: return "King";
                default: return rank.ToString();
            }
        }

        private string handText(List<int> hand)
        {
            List<string> names = new List<string>();
            foreach (int rank in hand)
                names.Add(cardName(rank));
            return string.Join(", ", names) + " (" + handValue(hand) + ")";
        }

        public void cmdBlackjack(string message)
        {
            Random r = new Random();
            message = message.Trim().ToLower();

            if (!bjInGame)
            {
                if (message != "" && message != "deal")
                {
                    sendToUser("Syntax: blackjack [hit/stand] - deals a new hand if you don't have one in progress", true, false, false);
                    return;
                }

                bjPlayerHand = new List<int> { r.Next(1, 14), r.Next(1, 14) };
                bjDealerHand = new List<int> { r.Next(1, 14), r.Next(1, 14) };
                bjInGame = true;

                sendToUser("Your hand: " + handText(bjPlayerHand), true, false, false);
                sendToUser("Dealer shows: " + cardName(bjDealerHand[0]), true, false, false);

                if (handValue(bjPlayerHand) == 21)
                {
                    sendToUser("^YBlackjack!^N", true, false, false);
                    bjResolve(true, false);
                }
                else
                {
                    sendToUser("blackjack hit, or blackjack stand?", true, false, false);
                }
            }
            else if (message == "hit")
            {
                bjPlayerHand.Add(r.Next(1, 14));
                sendToUser("Your hand: " + handText(bjPlayerHand), true, false, false);

                if (handValue(bjPlayerHand) > 21)
                {
                    sendToUser("^RBust!^N", true, false, false);
                    bjResolve(false, true);
                }
                else
                {
                    sendToUser("blackjack hit, or blackjack stand?", true, false, false);
                }
            }
            else if (message == "stand")
            {
                while (handValue(bjDealerHand) < 17)
                    bjDealerHand.Add(r.Next(1, 14));

                sendToUser("Dealer's hand: " + handText(bjDealerHand), true, false, false);

                int player = handValue(bjPlayerHand);
                int dealer = handValue(bjDealerHand);

                if (dealer > 21 || player > dealer)
                {
                    sendToUser(dealer > 21 ? "^GDealer busts - you win!^N" : "^GYou win!^N", true, false, false);
                    bjResolve(true, false);
                }
                else if (dealer > player)
                {
                    sendToUser("^RDealer wins^N", true, false, false);
                    bjResolve(false, true);
                }
                else
                {
                    sendToUser("Push - it's a draw", true, false, false);
                    bjResolve(false, false);
                }
            }
            else
            {
                sendToUser("You're already mid-hand - blackjack hit, or blackjack stand?", true, false, false);
            }
        }

        private void bjResolve(bool won, bool lost)
        {
            if (won)
                myPlayer.blackjack.won++;
            else if (lost)
                myPlayer.blackjack.lost++;
            else
                myPlayer.blackjack.drawn++;

            bjInGame = false;
            bjPlayerHand = null;
            bjDealerHand = null;
        }

        #endregion

    }
}
