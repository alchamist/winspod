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

        #region Object stuff

        public void cmdCreate(string message)
        {
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else if (message == "")
                sendToUser("Syntax: create <object name>", true, false, false);
            else
            {
                playerObjects = loadObjects();
                foreach (objects o in playerObjects)
                {
                    if (o.Name.ToLower() == message.ToLower())
                    {
                        sendToUser("That name already exists", true, false, false);
                        return;
                    }
                }
                objects newObj = new objects();
                newObj.Name = message;
                newObj.Creator = myPlayer.UserName;
                newObj.Owner = myPlayer.UserName;
                newObj.Gender = (gender)myPlayer.Gender;

                playerObjects.Add(newObj);
                saveObjects();
                sendToUser("Object \"" + newObj.Name + "\" created");
            }
        }

        public void cmdEdObj(string message)
        {
            string[] split = message.Split(new char[] { ' ' }, 3);
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else if (message == "" || split.Length < 2)
                sendToUser("Syntax: edobj <object name> <command part> <action>", true, false, false);
            else
            {
                playerObjects = loadObjects();
                for (int i = playerObjects.Count - 1; i >= 0; i--)
                {
                    if (playerObjects[i].Name.ToLower() == split[0].ToLower())
                    {
                        objects temp = playerObjects[i];
                        switch (split[1].ToLower())
                        {
                            case "drop":
                                temp.Actions.Drop = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Drop == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "eat":
                                temp.Actions.Eat = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Eat == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "drink":
                                temp.Actions.Drink = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Drink == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "examine":
                                temp.Actions.Examine = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Examine == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "get":
                                temp.Actions.Get = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Get == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "give":
                                temp.Actions.Give = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Give == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "pick":
                                temp.Actions.Pick = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Pick == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "play":
                                temp.Actions.Play = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Play == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "poke":
                                temp.Actions.Poke = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Poke == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "pull":
                                temp.Actions.Pull = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Pull == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "push":
                                temp.Actions.Push = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Push == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "shake":
                                temp.Actions.Shake = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Shake == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "take":
                                temp.Actions.Take = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Take == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "throw":
                                temp.Actions.Throw = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Throw == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "use":
                                temp.Actions.Use = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Use == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "wield":
                                temp.Actions.Wield = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Actions.Wield == "" ? "remove" : "set") + " the \"" + split[1] + "\" action for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "desc":
                                temp.Description = (split.Length < 3 ? "" : split[2]);
                                sendToUser("You " + (temp.Description == "" ? "remove" : "set") + " the description for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "weight":
                                int output;
                                if (split.Length == 3 && int.TryParse(split[2], out output))
                                {
                                    if (output >= 0)
                                    {
                                        temp.Weight = (split.Length < 3 ? 0 : output);
                                        sendToUser("You " + (temp.Weight == 0 ? "remove" : "set") + " the weight for object \"" + temp.Name + "\"", true, false, false);
                                    }
                                    else
                                        sendToUser("Negative weights are not allowed", true, false, false);
                                }
                                else
                                {
                                    sendToUser("Weight must be a positive integer", true, false, false);
                                }
                                break;
                            case "punique":
                                temp.Unique.ToPlayer = !temp.Unique.ToPlayer;
                                sendToUser("You " + (temp.Unique.ToPlayer ? "set" : "remove") + " the Unique for player flag for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "sunique":
                                temp.Unique.ToSystem = !temp.Unique.ToPlayer;
                                sendToUser("You " + (temp.Unique.ToSystem ? "set" : "remove") + " the Unique for whole system flag for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            case "candrop":
                                temp.CanDropOnFloor = !temp.CanDropOnFloor;
                                sendToUser("You " + (temp.CanDropOnFloor ? "set" : "remove") + " the \"Can drop on floor\" flag for object \"" + temp.Name + "\"", true, false, false);
                                break;
                            default:
                                sendToUser("Syntax: edobj <object name> <command part> <action>", true, false, false);
                                break;
                        }
                        playerObjects[i] = temp;
                        saveObjects();
                        return;
                    }
                }
                sendToUser("Object \"" + split[0] + "\" not found", true, false, false);
            }
        }

        public void cmdDelObj(string message)
        {
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else if (message == "")
                sendToUser("Syntax: delobj <object name>", true, false, false);
            else
            {
                playerObjects = loadObjects();
                for (int i = playerObjects.Count - 1; i >= 0; i--)
                {
                    if (playerObjects[i].Name.ToLower() == message.ToLower())
                    {
                        if (playerObjects[i].Creator.ToLower() == myPlayer.UserName.ToLower())
                        {
                            objects temp = playerObjects[i];
                            temp.Deleted = true;
                            playerObjects[i] = temp;
                            sendToUser("Object \"" + message + "\" deleted", true, false, false);
                            saveObjects();
                        }
                        else
                        {
                            sendToUser("You are not the owner of that object!", true, false, false);
                        }
                        return;
                    }
                }
                sendToUser("Object \"" + message + "\" not found", true, false, false);
            }
        }

        public void cmdExObj(string message)
        {
            if (!myPlayer.SpecialPrivs.builder)
                sendToUser("Sorry, you need builder privs for this command", true, false, false);
            else if (message == "")
                sendToUser("Syntax: exobj <object name>", true, false, false);
            else
            {
                playerObjects = loadObjects();

                objects s = new objects();
                s.Name = "Test";
                s.Owner = "Alchamist";
                s.Weight = 100;
                s.Unique.ToPlayer = true;
                s.Actions.Push = "Wibble";
                s.Actions.Take = "Plink";
                playerObjects.Add(s);

                foreach (objects o in playerObjects)
                {
                    if (o.Name.ToLower() == message.ToLower() && !o.Deleted)
                    {
                        string output = headerLine("Object: " + o.Name) + "\r\n";
                        output += "^BName: ^N" + o.Name.PadRight(14) + "^BOwner: ^N" + o.Owner.PadRight(15) + "^BWeight: ^N" + o.Weight.ToString().PadRight(5);
                        output += "\r\n^BUnique to Player: ^N" + (o.Unique.ToPlayer ? "Yes" : "No").PadRight(20) + "^BUnique to System: ^N" + (o.Unique.ToSystem ? "Yes" : "No");
                        output += "\r\n^BDescription: ^N" + o.Description + "\r\n";
                        output += footerLine() + "\r\n";
                        output += "^BActions:^N\r\n\r\n";
                        output += "^PGet: ^N".PadLeft(15) + o.Actions.Get + "\r\n";
                        output += "^PDrop: ^N".PadLeft(15) + o.Actions.Drop + "\r\n";
                        output += "^PDrink: ^N".PadLeft(15) + o.Actions.Drink + "\r\n";
                        output += "^PEat: ^N".PadLeft(15) + o.Actions.Eat + "\r\n";
                        output += "^PExamine: ^N".PadLeft(15) + o.Actions.Examine + "\r\n";
                        output += "^PGive: ^N".PadLeft(15) + o.Actions.Give + "\r\n";
                        output += "^PPick: ^N".PadLeft(15) + o.Actions.Pick + "\r\n";
                        output += "^PPlay: ^N".PadLeft(15) + o.Actions.Play + "\r\n";
                        output += "^PPoke: ^N".PadLeft(15) + o.Actions.Poke + "\r\n";
                        output += "^PPull: ^N".PadLeft(15) + o.Actions.Pull + "\r\n";
                        output += "^PPush: ^N".PadLeft(15) + o.Actions.Push + "\r\n";
                        output += "^PShake: ^N".PadLeft(15) + o.Actions.Shake + "\r\n";
                        output += "^PTake: ^N".PadLeft(15) + o.Actions.Take + "\r\n";
                        output += "^PThrow: ^N".PadLeft(15) + o.Actions.Throw + "\r\n";
                        output += "^PUse: ^N".PadLeft(15) + o.Actions.Use + "\r\n";
                        output += "^PWield: ^N".PadLeft(15) + o.Actions.Wield + "\r\n";
                        output += footerLine();
                        sendToUser(output, true, false, false);
                        return;
                    }
                }
                sendToUser("Object \"" + message + "\" not found", true, false, false);
            }
        }

        public void listObjects(string message)
        {
            playerObjects = loadObjects();

            string output = "";
            int place = 1;
            foreach (objects o in playerObjects)
            {
                if (!o.Deleted)
                {
                    if (message == "" || o.Name.StartsWith(message))
                        output += "^B( " + place++.ToString().PadLeft(playerObjects.Count.ToString().Length, '0') + " )^N " + (o.Unique.ToSystem ? "^Y*^N" : (o.Unique.ToPlayer ? "^R*^N" : " ")) + o.Name.PadRight(14) + "^BOwner: ^N" + o.Owner.PadRight(15) + "^BWeight: ^N" + o.Weight.ToString().PadRight(5) + "\r\n^BDescription: ^N" + o.Description + "\r\n";
                }
            }
            sendToUser(headerLine("Objects: " + (message == "" ? "All" : message)) + "\r\n" + (output == "" ? "No objects found" : output) + "\r\n" + footerLine(), true, false, false);
        }

        private int doObjectCode(string objectName, string action)
        {
            // Return codes - 0 = found and ok, 1 = not in inventory, 2 = object deleted, 3 = no code to run
            playerObjects = loadObjects();
            if (myPlayer.InInventory(objectName) == 0)
                return 1;
            foreach (objects o in playerObjects)
            {
                if (o.Name.ToLower() == objectName.ToLower())
                {
                    if (o.Deleted)
                        return 2;
                    else
                    {
                        #region Select Action ...

                        string code = "";
                        switch (action.ToLower())
                        {
                            case "drop":
                                code = o.Actions.Drop;
                                break;
                            case "eat":
                                code = o.Actions.Eat;
                                break;
                            case "drink":
                                code = o.Actions.Drink;
                                break;
                            case "examine":
                                code = o.Actions.Examine;
                                break;
                            case "get":
                                code = o.Actions.Get;
                                break;
                            case "give":
                                code = o.Actions.Give;
                                break;
                            case "pick":
                                code = o.Actions.Pick;
                                break;
                            case "play":
                                code = o.Actions.Play;
                                break;
                            case "poke":
                                code = o.Actions.Poke;
                                break;
                            case "pull":
                                code = o.Actions.Pull;
                                break;
                            case "push":
                                code = o.Actions.Push;
                                break;
                            case "shake":
                                code = o.Actions.Shake;
                                break;
                            case "take":
                                code = o.Actions.Take;
                                break;
                            case "throw":
                                code = o.Actions.Throw;
                                break;
                            case "use":
                                code = o.Actions.Use;
                                break;
                            case "wield":
                                code = o.Actions.Wield;
                                break;
                        }

                        #endregion

                        if (code != "")
                        {
                            string[] split = code.Split(new char[] { ';' });

                            #region Loop one - do the if statements

                            for (int i = 0; i < split.Length; i++)
                            {
                                string tCode = "";
                                switch (split[i].Substring(0, 4))
                                {
                                    case "%ipn": // If playerRank = newbie
                                        tCode = (myPlayer.PlayerRank == (int)Player.Rank.Newbie ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ipr": // If playerRank = resident
                                        tCode = (myPlayer.PlayerRank == (int)Player.Rank.Member ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ipg": // If playerRank = guide
                                        tCode = (myPlayer.PlayerRank == (int)Player.Rank.Guide ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ips": // If playerRank = staff
                                        tCode = (myPlayer.PlayerRank == (int)Player.Rank.Staff ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ipa": // If playerRank = admin
                                        tCode = (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ign": // If playerRank > newbie
                                        tCode = (myPlayer.PlayerRank > (int)Player.Rank.Newbie ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%igr": // If playerRank > resident
                                        tCode = (myPlayer.PlayerRank > (int)Player.Rank.Member ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%igg": // If playerRank > guide
                                        tCode = (myPlayer.PlayerRank > (int)Player.Rank.Guide ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%igs": // If playerRank > staff
                                        tCode = (myPlayer.PlayerRank > (int)Player.Rank.Staff ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ilg": // If playerRank < guide
                                        tCode = (myPlayer.PlayerRank < (int)Player.Rank.Guide ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ils": // If playerRank < staff
                                        tCode = (myPlayer.PlayerRank < (int)Player.Rank.Staff ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    case "%ila": // If playerRank < admin
                                        tCode = (myPlayer.PlayerRank < (int)Player.Rank.Admin ? split[i].Replace(split[i].Substring(0, 4), "") : "");
                                        break;
                                    default:
                                        tCode = split[i];
                                        break;
                                }
                                split[i] = tCode.Trim();
                            }

                            #endregion
                            #region Loop two - place in names etc

                            for (int i = 0; i < split.Length; i++)
                            {
                                split[i] = split[i].Replace("%pnm", myPlayer.UserName); // Player name
                                split[i] = split[i].Replace("%onm", o.Owner); // Owner name
                                split[i] = split[i].Replace("%cnm", o.Creator); // Creator name
                                split[i] = split[i].Replace("%obn", o.Name); // Object name

                                split[i] = split[i].Replace("%psp", (myPlayer.Gender == 0 ? "it" : (myPlayer.Gender == 1 ? "he" : "she"))); // Player subject pronoun (he/she)
                                split[i] = split[i].Replace("%pop", (myPlayer.Gender == 0 ? "it" : (myPlayer.Gender == 1 ? "him" : "her"))); // Player object pronoun (him/her)
                                split[i] = split[i].Replace("%pap", (myPlayer.Gender == 0 ? "its" : (myPlayer.Gender == 1 ? "his" : "her"))); // Player attributive pronoun (his/her)
                                split[i] = split[i].Replace("%ppn", (myPlayer.Gender == 0 ? "its" : (myPlayer.Gender == 1 ? "his" : "hers"))); // Player possesove pronoun (his/hers)
                            }

                            #endregion
                            #region Loop three - do the code!

                            for (int i = 0; i < split.Length; i++)
                            {
                                if (split[i] != "")
                                {
                                    switch (split[i].Substring(0, 4))
                                    {
                                        case "%stp": // Send to player
                                            sendToUser(split[i].Replace(split[i].Substring(0, 4), "").Trim());
                                            break;
                                        case "%str": // Send to room
                                            sendToRoom(split[i].Replace(split[i].Substring(0, 4), "").Trim(), "");
                                            break;
                                        case "%sta": // Send to all
                                            if (o.Rank >= Player.Rank.Admin)
                                                sendToAll(split[i].Replace(split[i].Substring(0, 4), "").Trim());
                                            break;
                                        case "%trn": // Transport player
                                            if (o.Rank >= Player.Rank.Admin)
                                                movePlayer(split[i].Replace(split[i].Substring(0, 4), "").Trim());
                                            break;
                                        case "%wib": // Wibble player
                                            if (o.Rank >= Player.Rank.Admin)
                                                myPlayer.Wibbled = true;
                                            break;
                                        case "%jal": // Jail player
                                            if (o.Rank >= Player.Rank.Admin)
                                                movePlayer("jail");
                                            break;
                                        case "%bmp": // Bump player
                                            if (o.Rank >= Player.Rank.Admin)
                                                socket.Close();
                                            break;
                                        case "%wld": // Wield
                                            myPlayer.WieldInventory(o.Name);
                                            break;
                                        case "%drp": // Drop item on floor
                                            if (o.CanDropOnFloor)
                                                dropOnFloor(o.Name);
                                            break;

                                    }
                                }
                            }
                            #endregion
                        }
                        else
                        {
                            return 3;
                        }
                    }
                }
            }
            return 0;
        }

        public void dropOnFloor(string itemName)
        {
            myPlayer.RemoveFromInventory(itemName);
            foreach (Room r in roomList)
            {
                if (r.systemName.ToLower() == myPlayer.UserRoom.ToLower())
                {
                    r.addObject(itemName);
                    return;
                }
            }
        }

        public void getFromFloor(string itemName)
        {
            myPlayer.RemoveFromInventory(itemName);
            foreach (Room r in roomList)
            {
                if (r.systemName.ToLower() == myPlayer.UserRoom.ToLower())
                {
                    r.removeObject(itemName);
                    r.SaveRoom();
                    return;
                }
            }
        }

        public void cmdInventory(string message)
        {
            string output = "";
            int totalWeight = 0;
            List<Player.inventory> inventory = myPlayer.Inventory;
            foreach (Player.inventory i in inventory)
            {
                objects inv = getObject(i.name);
                if (inv.Name != null && !inv.Deleted)
                {
                    output += "^p[" + i.count.ToString().PadLeft(3, '0') + "]^N " + inv.Name + " ^a(" + inv.Weight.ToString() + " units each)^N\r\n";
                    totalWeight += (i.count * inv.Weight);
                }
            }
            if (output == "")
                output = "You have no items\r\nYou can carry a total of " + myPlayer.MaxWeight.ToString() + " units of weight";
            else
                output = "^BQty  Item^N\r\n" + output + "\r\nYou are carrying a total of " + totalWeight.ToString() + " units of weight, and can carry " + (myPlayer.MaxWeight - totalWeight).ToString() + " more";

            sendToUser(headerLine("Inventory") + "\r\n" + output + "\r\n" + footerLine());
        }

        public void cmdEdWeight(string message)
        {
            string[] split = message.Split(new char[] { ' ' });
            int weightChange = 0;
            if (message == "" || split.Length != 2 || !int.TryParse(split[1], out weightChange))
                sendToUser("Syntax: edweight <player> <+/- units>", true, false, false);
            else
            {
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            c.myPlayer.MaxWeight += weightChange;
                            sendToUser("You change " + c.myPlayer.UserName + "'s Max weight to " + c.myPlayer.MaxWeight + " units", true, false, false);
                            c.sendToUser(myPlayer.ColourUserName + " has changed the maximum weight you can carry to " + c.myPlayer.MaxWeight + " units", true, false, false);
                            c.myPlayer.SavePlayer();
                            return;
                        }
                    }
                    sendToUser("Strange - something seems to have gone wrong ...", true, false, false);
                }
            }
        }

        private objects getObject(string objectName)
        {
            playerObjects = loadObjects();
            objects ret = new objects();
            foreach (objects o in playerObjects)
            {
                if (o.Name.ToLower() == objectName.ToLower())
                    ret = o;
            }
            return ret;
        }

        private int getInventoryWeight()
        {
            int totalWeight = 0;
            List<Player.inventory> inventory = myPlayer.Inventory;
            foreach (Player.inventory i in inventory)
            {
                objects inv = getObject(i.name);
                if (inv.Name != null && !inv.Deleted)
                {
                    totalWeight += (i.count & inv.Weight);
                }
            }
            return totalWeight;
        }

        public void cmdGet(string message)
        {
            if (message == "")
                sendToUser("Syntax: get <object name>", true, false, false);
            else
            {
                objects get = getObject(message);
                if (get.Name == null || get.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else
                {
                    if (getInventoryWeight() + get.Weight > myPlayer.MaxWeight)
                        sendToUser("Sorry, that is too heavy for you", true, false, false);
                    else if (myPlayer.InInventory(get.Name) > 0 && get.Unique.ToPlayer)
                        sendToUser("Sorry, you can only have one of those", true, false, false);
                    else
                    {
                        bool add = myPlayer.InInventory(get.Name) > 0;
                        myPlayer.AddToInventory(get.Name);

                        if (get.Actions.Get == null || get.Actions.Get == "")
                            sendToUser("You take " + (add ? "another " : (isVowel(get.Name.Substring(0, 1)) ? "an " : "a ")) + get.Name, true, false, false);
                        else
                        {
                            int status = doObjectCode(get.Name, "get");
                            if (status == 1 || status == 2)
                                sendToUser("Sorry - something seems to have gone wrong!", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdTake(string message)
        {
            if (message == "")
                sendToUser("Syntax: take <object name>", true, false, false);
            else
            {
                objects get = getObject(message);
                Room currentRoom = getRoom(myPlayer.UserRoom);

                if (get.Name == null || get.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (currentRoom.isObjectInRoom(get.Name) == 0)
                    sendToUser("You cannot see " + (isVowel(get.Name.Substring(0, 1)) ? "an " : "a ") + get.Name + " here");
                else
                {
                    if (getInventoryWeight() + get.Weight > myPlayer.MaxWeight)
                        sendToUser("Sorry, that is too heavy for you", true, false, false);
                    else if (myPlayer.InInventory(get.Name) > 0 && get.Unique.ToPlayer)
                        sendToUser("Sorry, you can only have one of those", true, false, false);
                    else
                    {
                        bool add = myPlayer.InInventory(get.Name) > 0;
                        getFromFloor(get.Name);

                        myPlayer.AddToInventory(get.Name);


                        if (get.Actions.Get == null || get.Actions.Get == "")
                            sendToUser("You take " + (add ? "another " : (isVowel(get.Name.Substring(0, 1)) ? "an " : "a ")) + get.Name, true, false, false);
                        else
                        {
                            int status = doObjectCode(get.Name, "get");
                            if (status == 1 || status == 2)
                                sendToUser("Sorry - something seems to have gone wrong!", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdDrop(string message)
        {
            if (message == "")
                sendToUser("Syntax: drop <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Drop == null || target.Actions.Drop == "")
                    {
                        sendToUser("You drop " + (moreThanOne ? "one of your " : "your ") + target.Name + (target.Name.ToLower().EndsWith("s") ? "" : (moreThanOne ? "s" : "")), true, false, false);
                        //
                        if (target.CanDropOnFloor)
                            dropOnFloor(target.Name);
                        else
                            myPlayer.RemoveFromInventory(target.Name);
                    }
                    else
                    {
                        if (target.Rank < Player.Rank.Admin)
                            myPlayer.RemoveFromInventory(target.Name);
                        doObjectCode(target.Name, "drop");
                    }
                }
            }
        }

        public void cmdClearRoom(string message)
        {
            foreach (Room r in roomList)
            {
                if (r.systemName.ToLower() == myPlayer.UserRoom.ToLower())
                {
                    r.removeAllObjects();
                    sendToUser("You remove all objects from the floor of the room", true, false, false);
                    logToFile(myPlayer.UserName + " clears up the objects in " + r.fullName, "admin");
                    return;
                }
            }
        }

        public void cmdPlay(string message)
        {
            if (message == "")
                sendToUser("Syntax: play <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Play == null || target.Actions.Play == "")
                    {
                        sendToUser("You play with your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "play");
                    }
                }
            }
        }

        public void cmdEat(string message)
        {
            if (message == "")
                sendToUser("Syntax: eat <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Eat == null || target.Actions.Eat == "")
                    {
                        sendToUser("You find the " + target.Name + " inedible", true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "eat");
                    }
                }
            }
        }

        public void cmdDrink(string message)
        {
            if (message == "")
                sendToUser("Syntax: drink <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Drink == null || target.Actions.Drink == "")
                    {
                        sendToUser("You nearly choke to death trying to drink the " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "drink");
                    }
                }
            }
        }

        public void cmdPick(string message)
        {
            if (message == "")
                sendToUser("Syntax: pick <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Pick == null || target.Actions.Pick == "")
                    {
                        sendToUser("Nothing happens as you pick your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "pick");
                    }
                }
            }
        }

        public void cmdThrow(string message)
        {
            if (message == "")
                sendToUser("Syntax: throw <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Throw == null || target.Actions.Throw == "")
                    {
                        sendToUser("Nothing happens as you throw your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "throw");
                    }
                }
            }
        }

        public void cmdPush(string message)
        {
            if (message == "")
                sendToUser("Syntax: push <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Push == null || target.Actions.Push == "")
                    {
                        sendToUser("Nothing happens as you push your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "push");
                    }
                }
            }
        }

        public void cmdPull(string message)
        {
            if (message == "")
                sendToUser("Syntax: pull <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Pull == null || target.Actions.Pull == "")
                    {
                        sendToUser("Nothing happens as you push your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "pull");
                    }
                }
            }
        }

        public void cmdShake(string message)
        {
            if (message == "")
                sendToUser("Syntax: shake <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Shake == null || target.Actions.Shake == "")
                    {
                        sendToUser("Nothing happens as you shake your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "shake");
                    }
                }
            }
        }

        public void cmdPoke(string message)
        {
            if (message == "")
                sendToUser("Syntax: poke <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Poke == null || target.Actions.Poke == "")
                    {
                        sendToUser("Nothing happens as you poke your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "poke");
                    }
                }
            }
        }

        public void cmdUse(string message)
        {
            if (message == "")
                sendToUser("Syntax: use <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Use == null || target.Actions.Use == "")
                    {
                        sendToUser("You cannot seem to find a way to use your " + target.Name, true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "use");
                    }
                }
            }
        }

        public void cmdWield(string message)
        {
            if (message == "")
                sendToUser("Syntax: wield <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Wield == null || target.Actions.Wield == "")
                    {
                        sendToUser("You cannot seem to find a way to wield your " + target.Name, true, false, false);
                    }
                    else
                    {
                        myPlayer.WieldInventory(target.Name);
                        if (myPlayer.IsWielded(target.Name))
                        {
                            doObjectCode(target.Name, "wield");
                        }
                        else
                        {
                            sendToUser("You stop wielding your " + target.Name, true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdObjExamine(string message)
        {
            if (message == "")
                sendToUser("Syntax: examine <object name>", true, false, false);
            else
            {
                objects target = getObject(message);
                if (target.Name == null || target.Name == "")
                    sendToUser("Object \"" + message + "\" not found", true, false, false);
                else if (myPlayer.InInventory(target.Name) == 0)
                    sendToUser("You don't have " + (isVowel(target.Name.Substring(0, 1)) ? "an " : "a ") + target.Name);
                else
                {
                    bool moreThanOne = myPlayer.InInventory(target.Name) > 1;

                    if (target.Actions.Examine == null || target.Actions.Examine == "")
                    {
                        sendToUser("You see nothing special", true, false, false);
                    }
                    else
                    {
                        doObjectCode(target.Name, "examine");
                    }
                }
            }
        }

        public bool isVowel(string check)
        {
            check = check.ToLower();
            return (check == "a" || check == "e" || check == "i" || check == "o" || check == "u");
        }

        public void saveObjects()
        {
            try
            {
                string path = Path.Combine(Server.userFilePath, @"objects" + Path.DirectorySeparatorChar);
                string fname = "objects.xml";
                string fpath = path + fname;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                XmlSerializer serial = new XmlSerializer(typeof(List<objects>));
                TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                serial.Serialize(textWriter, playerObjects);
                textWriter.Close();
            }
            catch (Exception ex)
            {
                Connection.logError(ex.ToString(), "filesystem");
            }
        }

        public List<objects> loadObjects()
        {
            List<objects> load = new List<objects>();
            string path = Path.Combine(Server.userFilePath, @"objects" + Path.DirectorySeparatorChar);
            string fname = "objects.xml";
            string fpath = path + fname;

            if (Directory.Exists(path) && File.Exists(fpath))
            {
                try
                {
                    XmlSerializer deserial = new XmlSerializer(typeof(List<objects>));
                    TextReader textReader = new StreamReader(@fpath);
                    load = (List<objects>)deserial.Deserialize(textReader);
                    textReader.Close();
                }
                catch (Exception e)
                {
                    Debug.Print(e.ToString());
                }
            }
            return load;
        }

        #endregion

    }
}
