using System;
using System.Collections.Generic;
using System.Linq;

namespace MudServer
{
    public partial class Connection
    {

        #region Tic-tac-toe

        private class TicTacToeGame
        {
            public string PlayerX;
            public string PlayerO;
            public char[] Board = new char[] { '1', '2', '3', '4', '5', '6', '7', '8', '9' };
            public bool XTurn = true;
        }

        private static readonly int[,] tttLines = new int[,]
        {
            { 0, 1, 2 }, { 3, 4, 5 }, { 6, 7, 8 },
            { 0, 3, 6 }, { 1, 4, 7 }, { 2, 5, 8 },
            { 0, 4, 8 }, { 2, 4, 6 }
        };

        private string tttChallenger = null;
        private TicTacToeGame tttGame = null;

        private char? tttCheckWin(char[] board)
        {
            for (int i = 0; i < 8; i++)
            {
                char a = board[tttLines[i, 0]], b = board[tttLines[i, 1]], c = board[tttLines[i, 2]];
                if ((a == 'X' || a == 'O') && a == b && b == c)
                    return a;
            }
            return null;
        }

        private string tttRenderBoard(char[] b)
        {
            return "\r\n " + b[0] + " | " + b[1] + " | " + b[2] +
                   "\r\n---+---+---\r\n " + b[3] + " | " + b[4] + " | " + b[5] +
                   "\r\n---+---+---\r\n " + b[6] + " | " + b[7] + " | " + b[8] + "\r\n";
        }

        private Connection tttFindOpponent()
        {
            foreach (Connection c in connections)
            {
                if (c != this && c.tttGame == tttGame)
                    return c;
            }
            return null;
        }

        public void cmdTTT(string message)
        {
            message = message.Trim();

            if (tttGame != null && message.Length >= 1 && message.Length <= 1 && char.IsDigit(message[0]))
            {
                int cell = int.Parse(message) - 1;
                bool imX = tttGame.PlayerX.ToLower() == myPlayer.UserName.ToLower();

                if (cell < 0 || cell > 8)
                    sendToUser("Pick a cell from 1-9", true, false, false);
                else if (tttGame.XTurn != imX)
                    sendToUser("It's not your turn", true, false, false);
                else if (tttGame.Board[cell] == 'X' || tttGame.Board[cell] == 'O')
                    sendToUser("That cell is already taken", true, false, false);
                else
                {
                    tttGame.Board[cell] = imX ? 'X' : 'O';
                    tttGame.XTurn = !tttGame.XTurn;

                    char? winner = tttCheckWin(tttGame.Board);
                    bool full = !tttGame.Board.Any(ch => ch != 'X' && ch != 'O');
                    Connection opp = tttFindOpponent();
                    string board = tttRenderBoard(tttGame.Board);

                    if (winner != null)
                    {
                        bool iWon = (winner == 'X') == imX;

                        sendToUser(board, true, false, false);
                        if (opp != null) opp.sendToUser(board, true, false, false);
                        sendToUser(iWon ? "^GYou win!^N" : "^RYou lose!^N", true, false, false);
                        if (opp != null) opp.sendToUser(iWon ? "^RYou lose!^N" : "^GYou win!^N", true, false, false);

                        if (iWon)
                        {
                            myPlayer.tictactoe.won++;
                            if (opp != null) opp.myPlayer.tictactoe.lost++;
                        }
                        else
                        {
                            myPlayer.tictactoe.lost++;
                            if (opp != null) opp.myPlayer.tictactoe.won++;
                        }
                        myPlayer.SavePlayer();
                        if (opp != null) opp.myPlayer.SavePlayer();

                        tttGame = null;
                        if (opp != null) opp.tttGame = null;
                    }
                    else if (full)
                    {
                        sendToUser(board + "\r\nIt's a draw!", true, false, false);
                        if (opp != null) opp.sendToUser(board + "\r\nIt's a draw!", true, false, false);

                        myPlayer.tictactoe.drawn++;
                        if (opp != null) opp.myPlayer.tictactoe.drawn++;
                        myPlayer.SavePlayer();
                        if (opp != null) opp.myPlayer.SavePlayer();

                        tttGame = null;
                        if (opp != null) opp.tttGame = null;
                    }
                    else
                    {
                        sendToUser(board, true, false, false);
                        if (opp != null) opp.sendToUser(board + "\r\nYour turn - ttt <cell>", true, true, false);
                    }
                }
            }
            else if (message == "")
            {
                if (tttGame != null)
                    sendToUser(tttRenderBoard(tttGame.Board), true, false, false);
                else
                    sendToUser("Syntax: ttt <player> to challenge, or ttt <cell 1-9> to play", true, false, false);
            }
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("You can't play against yourself!", true, false, false);
                else if (tttGame != null)
                    sendToUser("You're already playing a game - ttt <cell> to play, or finish it first", true, false, false);
                else
                {
                    Connection targConn = null;
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                            targConn = c;
                    }

                    if (targConn == null)
                        sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                    else if (targConn.tttGame != null)
                        sendToUser(targConn.myPlayer.ColourUserName + " is already playing a game", true, false, false);
                    else if (tttChallenger != null && tttChallenger.ToLower() == target[0].ToLower())
                    {
                        // I have a pending challenge FROM target[0] (recorded on my own
                        // connection when they issued it) - accept it. The check has to be
                        // on my own tttChallenger, not targConn's - that field means "who
                        // has challenged the owner of this connection", so accepting means
                        // reading my own, not theirs.
                        TicTacToeGame game = new TicTacToeGame();
                        game.PlayerX = targConn.myPlayer.UserName; // the original challenger goes first
                        game.PlayerO = myPlayer.UserName;
                        tttGame = game;
                        targConn.tttGame = game;
                        tttChallenger = null;

                        string board = tttRenderBoard(game.Board);
                        sendToUser("You accept " + targConn.myPlayer.ColourUserName + "'s challenge! You are O." + board, true, false, false);
                        targConn.sendToUser(myPlayer.ColourUserName + " accepts your challenge! You are X." + board + "\r\nYour turn - ttt <cell>", true, true, false);
                    }
                    else
                    {
                        targConn.tttChallenger = myPlayer.UserName;
                        sendToUser("You challenge " + targConn.myPlayer.ColourUserName + " to a game of tic-tac-toe!", true, false, false);
                        targConn.sendToUser(myPlayer.ColourUserName + " challenges you to a game of tic-tac-toe! Type \"ttt " + myPlayer.UserName + "\" to accept.", true, true, false);
                    }
                }
            }
        }

        #endregion

        #region Rock-paper-scissors

        private class RpsGame
        {
            public string PlayerA;
            public string PlayerB;
            public string ChoiceA;
            public string ChoiceB;
        }

        private string rpsChallenger = null;
        private RpsGame rpsGame = null;

        private Connection rpsFindOpponent()
        {
            foreach (Connection c in connections)
            {
                if (c != this && c.rpsGame == rpsGame)
                    return c;
            }
            return null;
        }

        private void rpsResolve(Connection opp)
        {
            if (opp == null)
            {
                sendToUser("Your opponent isn't here any more - game abandoned", true, false, false);
                rpsGame = null;
                return;
            }

            bool imA = rpsGame.PlayerA.ToLower() == myPlayer.UserName.ToLower();
            Connection connA = imA ? this : opp;
            Connection connB = imA ? opp : this;
            string a = rpsGame.ChoiceA;
            string b = rpsGame.ChoiceB;

            string result; // "A", "B", or "draw"
            if (a == b)
                result = "draw";
            else if ((a == "rock" && b == "scissors") || (a == "paper" && b == "rock") || (a == "scissors" && b == "paper"))
                result = "A";
            else
                result = "B";

            string summary = connA.myPlayer.ColourUserName + " chose " + a + ", " + connB.myPlayer.ColourUserName + " chose " + b + ".";
            connA.sendToUser(summary, true, false, false);
            connB.sendToUser(summary, true, false, false);

            if (result == "draw")
            {
                connA.sendToUser("It's a draw!", true, false, false);
                connB.sendToUser("It's a draw!", true, false, false);
                connA.myPlayer.rps.drawn++;
                connB.myPlayer.rps.drawn++;
            }
            else
            {
                Connection winner = result == "A" ? connA : connB;
                Connection loser = result == "A" ? connB : connA;
                winner.sendToUser("^GYou win!^N", true, false, false);
                loser.sendToUser("^RYou lose!^N", true, false, false);
                winner.myPlayer.rps.won++;
                loser.myPlayer.rps.lost++;
            }

            connA.myPlayer.SavePlayer();
            connB.myPlayer.SavePlayer();

            connA.rpsGame = null;
            connB.rpsGame = null;
        }

        public void cmdRPS(string message)
        {
            message = message.Trim().ToLower();
            string choice = message == "r" || message == "rock" ? "rock"
                           : message == "p" || message == "paper" ? "paper"
                           : message == "s" || message == "scissors" ? "scissors"
                           : null;

            if (choice != null)
            {
                if (rpsGame == null)
                    sendToUser("You're not in a game - rps <player> to challenge someone first", true, false, false);
                else
                {
                    bool imA = rpsGame.PlayerA.ToLower() == myPlayer.UserName.ToLower();
                    if ((imA && rpsGame.ChoiceA != null) || (!imA && rpsGame.ChoiceB != null))
                        sendToUser("You've already chosen - waiting on your opponent", true, false, false);
                    else
                    {
                        if (imA) rpsGame.ChoiceA = choice; else rpsGame.ChoiceB = choice;
                        sendToUser("You choose " + choice + ".", true, false, false);

                        if (rpsGame.ChoiceA != null && rpsGame.ChoiceB != null)
                            rpsResolve(rpsFindOpponent());
                        else
                            sendToUser("Waiting for your opponent to choose...", true, false, false);
                    }
                }
            }
            else if (message == "")
            {
                sendToUser(rpsGame != null
                    ? "Syntax: rps rock/paper/scissors"
                    : "Syntax: rps <player> to challenge, or rps rock/paper/scissors to play", true, false, false);
            }
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("You can't play against yourself!", true, false, false);
                else if (rpsGame != null)
                    sendToUser("You're already playing a game", true, false, false);
                else
                {
                    Connection targConn = null;
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                            targConn = c;
                    }

                    if (targConn == null)
                        sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                    else if (targConn.rpsGame != null)
                        sendToUser(targConn.myPlayer.ColourUserName + " is already playing a game", true, false, false);
                    else if (rpsChallenger != null && rpsChallenger.ToLower() == target[0].ToLower())
                    {
                        // See the equivalent ttt check's comment - this has to read my own
                        // rpsChallenger (who has challenged me), not targConn's.
                        RpsGame game = new RpsGame();
                        game.PlayerA = targConn.myPlayer.UserName;
                        game.PlayerB = myPlayer.UserName;
                        rpsGame = game;
                        targConn.rpsGame = game;
                        rpsChallenger = null;

                        sendToUser("You accept " + targConn.myPlayer.ColourUserName + "'s challenge! rps rock/paper/scissors to choose.", true, false, false);
                        targConn.sendToUser(myPlayer.ColourUserName + " accepts your challenge! rps rock/paper/scissors to choose.", true, true, false);
                    }
                    else
                    {
                        targConn.rpsChallenger = myPlayer.UserName;
                        sendToUser("You challenge " + targConn.myPlayer.ColourUserName + " to rock-paper-scissors!", true, false, false);
                        targConn.sendToUser(myPlayer.ColourUserName + " challenges you to rock-paper-scissors! Type \"rps " + myPlayer.UserName + "\" to accept.", true, true, false);
                    }
                }
            }
        }

        #endregion

    }
}
