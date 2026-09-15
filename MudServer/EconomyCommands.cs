using System;
using System.Collections.Generic;

namespace MudServer
{
    public partial class Connection
    {

        #region Economy stuff

        public void cmdBalance(string message)
        {
            sendToUser("You have " + myPlayer.Credits + " credit" + (myPlayer.Credits == 1 ? "" : "s"), true, false, false);
        }

        public void cmdPay(string message)
        {
            string[] split = message.Split(new char[] { ' ' });
            int amount;

            if (message == "" || split.Length != 2 || !int.TryParse(split[1], out amount))
                sendToUser("Syntax: pay <player> <amount>", true, false, false);
            else if (amount <= 0)
                sendToUser("The amount must be a positive number", true, false, false);
            else if (myPlayer.Credits < amount)
                sendToUser("You don't have that many credits", true, false, false);
            else
            {
                string[] targPlayer = matchPartial(split[0]);
                if (targPlayer.Length == 0)
                    sendToUser("Player \"" + split[0] + "\" not found", true, false, false);
                else if (targPlayer.Length > 1)
                    sendToUser("Multiple matches found: " + targPlayer.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(targPlayer[0]))
                    sendToUser("Player \"" + targPlayer[0] + "\" is not online", true, false, false);
                else if (targPlayer[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("You can't pay yourself!", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == targPlayer[0].ToLower())
                        {
                            myPlayer.Credits -= amount;
                            myPlayer.SavePlayer();
                            c.myPlayer.Credits += amount;
                            c.myPlayer.SavePlayer();

                            sendToUser("You pay " + amount + " credit" + (amount == 1 ? "" : "s") + " to " + c.myPlayer.ColourUserName, true, false, false);
                            c.sendToUser("\r\n" + myPlayer.ColourUserName + " pays you " + amount + " credit" + (amount == 1 ? "" : "s"), true, true, false);
                            return;
                        }
                    }
                }
            }
        }

        public void cmdSell(string message)
        {
            string[] split = message.Split(new char[] { ' ' });
            int price;

            if (message == "" || split.Length != 2 || !int.TryParse(split[1], out price))
                sendToUser("Syntax: sell <object name> <price>", true, false, false);
            else if (price <= 0)
                sendToUser("The price must be a positive number", true, false, false);
            else
            {
                objects target = getObject(split[0]);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + split[0] + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    Room currentRoom = getRoom(myPlayer.UserRoom);
                    myPlayer.RemoveFromInventory(target.Name);
                    currentRoom.addListing(myPlayer.UserName, target.Name, price);
                    sendToUser("You list " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name + " for sale here for " + price + " credit" + (price == 1 ? "" : "s"), true, false, false);
                }
            }
        }

        public void cmdBuy(string message)
        {
            if (message == "")
                sendToUser("Syntax: buy <object name>", true, false, false);
            else
            {
                Room currentRoom = getRoom(myPlayer.UserRoom);
                int matchIndex = -1;
                for (int i = 0; i < currentRoom.shopListings.Count; i++)
                {
                    if (currentRoom.shopListings[i].objectName.ToLower() == message.ToLower())
                    {
                        matchIndex = i;
                        break;
                    }
                }

                if (matchIndex == -1)
                    sendToUser("There is nothing called \"" + message + "\" for sale here", true, false, false);
                else
                {
                    Room.shopListing listing = currentRoom.shopListings[matchIndex];

                    if (listing.seller.ToLower() == myPlayer.UserName.ToLower())
                        sendToUser("You can't buy your own listing - use unsell instead", true, false, false);
                    else
                    {
                        objects obj = getObject(listing.objectName);

                        if (myPlayer.Credits < listing.price)
                            sendToUser("You don't have enough credits - it costs " + listing.price, true, false, false);
                        else if (getInventoryWeight() + obj.Weight > myPlayer.MaxWeight)
                            sendToUser("Sorry, that is too heavy for you", true, false, false);
                        else if (myPlayer.InInventory(obj.Name) > 0 && obj.Unique.ToPlayer)
                            sendToUser("Sorry, you can only have one of those", true, false, false);
                        else
                        {
                            myPlayer.Credits -= listing.price;
                            myPlayer.SavePlayer();
                            myPlayer.AddToInventory(obj.Name);
                            currentRoom.removeListing(listing.seller, listing.objectName);

                            bool sellerOnline = false;
                            foreach (Connection c in connections)
                            {
                                if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == listing.seller.ToLower())
                                {
                                    c.myPlayer.Credits += listing.price;
                                    c.myPlayer.SavePlayer();
                                    c.sendToUser("\r\n" + myPlayer.ColourUserName + " buys your " + obj.Name + " for " + listing.price + " credit" + (listing.price == 1 ? "" : "s"), true, true, false);
                                    sellerOnline = true;
                                }
                            }
                            if (!sellerOnline)
                            {
                                Player seller = Player.LoadPlayer(listing.seller, 0);
                                if (seller != null && !seller.NewPlayer)
                                {
                                    seller.Credits += listing.price;
                                    seller.SavePlayer();
                                }
                            }

                            sendToUser("You buy " + (isVowel(obj.Name.Substring(0, 1)) ? "an " : "a ") + obj.Name + " from " + listing.seller + " for " + listing.price + " credit" + (listing.price == 1 ? "" : "s"), true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdUnsell(string message)
        {
            if (message == "")
                sendToUser("Syntax: unsell <object name>", true, false, false);
            else
            {
                Room currentRoom = getRoom(myPlayer.UserRoom);
                int matchIndex = -1;
                for (int i = 0; i < currentRoom.shopListings.Count; i++)
                {
                    if (currentRoom.shopListings[i].seller.ToLower() == myPlayer.UserName.ToLower() && currentRoom.shopListings[i].objectName.ToLower() == message.ToLower())
                    {
                        matchIndex = i;
                        break;
                    }
                }

                if (matchIndex == -1)
                    sendToUser("You don't have anything called \"" + message + "\" listed for sale here", true, false, false);
                else
                {
                    string objectName = currentRoom.shopListings[matchIndex].objectName;
                    currentRoom.removeListing(myPlayer.UserName, objectName);
                    myPlayer.AddToInventory(objectName);
                    sendToUser("You take your " + objectName + " off the market", true, false, false);
                }
            }
        }

        public void cmdShop(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            string output = "";
            int place = 1;
            foreach (Room.shopListing listing in currentRoom.shopListings)
            {
                output += "^B( " + place++.ToString().PadLeft(currentRoom.shopListings.Count.ToString().Length, '0') + " )^N " + listing.objectName.PadRight(14) + "^BPrice: ^N" + listing.price.ToString().PadRight(8) + "^BSeller: ^N" + listing.seller + "\r\n";
            }
            sendToUser(headerLine("For sale here") + "\r\n" + (output == "" ? "Nothing is for sale here\r\n" : output) + footerLine(), true, false, false);
        }

        public void cmdAward(string message)
        {
            string syntax = "Syntax: award <player> <amount>";
            string[] split = message.Split(new char[] { ' ' });
            int amount;

            if (myPlayer.PlayerRank < (int)Player.Rank.Admin)
                sendToUser("Sorry, you need admin privs for this command", true, false, false);
            else if (message == "" || split.Length != 2 || !int.TryParse(split[1], out amount))
                sendToUser(syntax, true, false, false);
            else if (amount <= 0)
                sendToUser("The amount must be a positive number", true, false, false);
            else
            {
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("Player \"" + split[0] + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else
                {
                    Player t = null;
                    bool online = false;

                    if (!isOnline(target[0]))
                    {
                        t = Player.LoadPlayer(target[0], 0);
                    }
                    else
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                            {
                                t = c.myPlayer;
                                online = true;
                            }
                        }
                    }

                    if (t == null)
                        sendToUser("Strange - something's gone wrong here ...", true, false, false);
                    else if (t.PlayerRank == (int)Player.Rank.Newbie)
                        sendToUser(t.UserName + " needs to be a resident first!", true, false, false);
                    else
                    {
                        t.Credits += amount;
                        t.SavePlayer();
                        sendToUser("You award " + amount + " credit" + (amount == 1 ? "" : "s") + " to " + t.UserName, true, false, false);
                        if (online)
                            sendToUser(myPlayer.UserName + " awards you " + amount + " credit" + (amount == 1 ? "" : "s"), t.UserName, true, t.DoColour, false, false);
                        logToFile(myPlayer.UserName + " awards " + amount + " credits to " + t.UserName, "grant");
                    }
                }
            }
        }

        #endregion

    }
}
