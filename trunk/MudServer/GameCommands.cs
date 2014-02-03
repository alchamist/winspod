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

        
    }
}
