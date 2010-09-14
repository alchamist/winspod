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

        #region Room Stuff


        public void cmdHideMe(string message)
        {
            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                if (myPlayer.Invisible)
                    sendToUser("You are now visible!", true, false, false);
                else
                    sendToUser("You are now invisible!", true, false, false);
                myPlayer.Invisible = !myPlayer.Invisible;
            }
        }

        public void cmdWhere(string message)
        {
            if (message != "")
            {
                string[] target = matchPartial(message);
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
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            sendToUser(c.myPlayer.ColourUserName + " is " + (c.myPlayer.Hidden && !c.myPlayer.CanFindMe(myPlayer.UserName) ? "hiding" : (c.myPlayer.Hidden && c.myPlayer.CanFindMe(myPlayer.UserName) ? "hiding " : "") + "in " + getRoomFullName(c.myPlayer.UserRoom)), true, false, false);
                        }
                    }
                }
            }
            else
            {
                string output = "";
                foreach (Connection c in connections)
                {
                    if (c.socket.Connected && c.myPlayer != null)
                    {
                        if (c.myPlayer.UserName == myPlayer.UserName)
                            output += "You are " + (myPlayer.Hidden || myPlayer.Invisible ? "hiding " : "") + "in " + getRoomFullName(myPlayer.UserRoom) + "\r\n";
                        else if (!c.myPlayer.Invisible && (!c.myPlayer.Hidden || myPlayer.PlayerRank >= (int)Player.Rank.Admin) || c.myPlayer.CanFindMe(myPlayer.UserName))
                            output += c.myPlayer.ColourUserName + " is in " + getRoomFullName(c.myPlayer.UserRoom) + (c.myPlayer.Hidden ? " (hidden)" : "") + "\r\n";
                        else if (!c.myPlayer.Invisible)
                            output += c.myPlayer.ColourUserName + " is hiding\r\n";
                        else
                            output += c.myPlayer.ColourUserName + " doesn't seem to be online right now\r\n";
                    }
                }
                sendToUser("{bold}{cyan}---[{red}Where{cyan}]".PadRight(103, '-') + "{reset}\r\n" + (output == "" ? "Strange, there's no one here ... not even you!" : output + "{bold}{cyan}" + "".PadRight(80, '-') + "{reset}"), true, false, false);
            }
        }

        public void cmdExits(string message)
        {
            Room room = getRoom(myPlayer.UserRoom);
            if (room == null)
                sendToUser("Strange - you don't seem to be anywhere!", true, false, false);
            else
            {
                string output = "";
                foreach (string s in room.exits)
                {
                    if (s != "")
                    {
                        Room exit = Room.LoadRoom(s);
                        output += exit.shortName + " -> " + getRoomFullName(s) + "\r\n";
                    }
                }
                if (output == "")
                    sendToUser("There are no exits from this room", true, false, false);
                else
                    sendToUser("The following exits are availale:\r\n" + output, true, false, false);
            }
        }

        public void cmdHidePlayer(string message)
        {
            sendToUser((myPlayer.Hidden) ? "You stop hiding" : "You are now hiding", true, false, false);
            myPlayer.Hidden = !myPlayer.Hidden;
            myPlayer.SavePlayer();
        }

        public void cmdGo(string message)
        {
            if (message == "")
                sendToUser("Syntax: Go <exit name>", true, false, false);
            else
            {
                Room currentRoom = getRoom(myPlayer.UserRoom);
                if (currentRoom != null)
                {
                    bool found = false;
                    foreach (string s in currentRoom.exits)
                    {
                        Room exit = Room.LoadRoom(s);
                        if (exit.fullName != null)
                        {
                            if (exit.shortName.ToLower() == message.ToLower() || exit.shortName.ToLower().StartsWith(message.ToLower()))
                            {
                                found = true;
                                movePlayer(s);
                            }
                        }
                    }
                    if (!found)
                    {
                        foreach (Room r in roomList)
                        {
                            if (r.systemName.ToLower() == message.ToLower())
                            {
                                found = true;
                                Player owner = null;
                                if (!r.systemRoom)
                                {
                                    owner = Player.LoadPlayer(r.roomOwner, 0);
                                }
                                if (((r.locks.FullLock || (r.locks.AdminLock && myPlayer.PlayerRank < (int)Player.Rank.Admin) || (r.locks.StaffLock && myPlayer.PlayerRank < (int)Player.Rank.Staff) || (r.locks.GuideLock && myPlayer.PlayerRank < (int)Player.Rank.Guide) || (!r.systemRoom && r.locks.FriendLock && owner != null && !owner.isFriend(myPlayer.UserName))) && myPlayer.PlayerRank < (int)Player.Rank.HCAdmin) && !owner.HasKey(myPlayer.UserName))
                                {
                                    sendToUser("You try the door, but find it locked shut", true, false, false);
                                }
                                else
                                {
                                    movePlayer(r.systemName);
                                }
                            }
                        }
                        if (!found)
                        {
                            sendToUser("No such room", true, false, false);
                        }
                    }
                }
                else
                {
                    sendToUser("Strange. You don't seem to be in a room at all ...", true, false, false);
                }
            }
        }

        public void cmdHome(string message)
        {
            // First check to see if they have a home room, and if not then create it!
            string roomName = myPlayer.UserName.ToLower() + ".home";
            Room check = Room.LoadRoom(roomName);
            if (check == null || check.fullName == null)
            {
                check = new Room("home", myPlayer.UserName, false);
                check.fullName = myPlayer.UserName + "'" + (myPlayer.UserName.EndsWith("s") ? "" : "s") + " little home";
                check.SaveRoom();
                roomList = loadRooms();
            }
            movePlayer(check.systemName);
        }

        public void cmdJoin(string message)
        {
            if (message == "")
                sendToUser("Syntax: join <player name>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
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
                        if (c.socket.Connected && c.myPlayer != null)
                        {
                            if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                            {
                                if (c.myPlayer.Hidden && !c.myPlayer.CanFindMe(myPlayer.UserName))
                                    sendToUser("Try as you might, you cannot seem to find " + target[0] + " anywhere!", true, false, false);
                                else
                                {
                                    Room targetRoom = Room.LoadRoom(c.myPlayer.UserRoom);
                                    if ((targetRoom.locks.FullLock || (targetRoom.locks.AdminLock && myPlayer.PlayerRank < (int)Player.Rank.Admin) || (targetRoom.locks.StaffLock && myPlayer.PlayerRank < (int)Player.Rank.Staff) || (targetRoom.locks.GuideLock && myPlayer.PlayerRank < (int)Player.Rank.Guide) || (targetRoom.locks.FriendLock && !c.myPlayer.isFriend(myPlayer.UserName))) && !c.myPlayer.HasKey(myPlayer.UserName))
                                    {
                                        sendToUser("You try the door, but find it locked shut", true, false, false);
                                    }
                                    else
                                    {
                                        movePlayer(c.myPlayer.UserRoom);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public void cmdMain(string message)
        {
            if (myPlayer.UserRoom == AppSettings.Default.DefaultLoginRoom)
                sendToUser("You are already in the main room!", true, false, false);
            else
                movePlayer(AppSettings.Default.DefaultLoginRoom);
        }

        public void cmdLook(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (currentRoom != null)
            {
                int center = (40 - (int)(currentRoom.fullName.Length / 2)) + currentRoom.fullName.Length;
                sendToUser("{bold}{cyan}".PadRight(92, '-') + "{reset}\r\n" + currentRoom.fullName.PadLeft(center, ' ') + "\r\n" + "{bold}{cyan}".PadRight(92, '-') + "{reset}\r\n" + currentRoom.description + "\r\n{bold}{cyan}".PadRight(94, '-') + "{reset}\r\n", true, false, false);
                doWhoRoom();

                List<Room.roomObjects> contents = currentRoom.roomContents;
                string objects = "";
                foreach (Room.roomObjects o in contents)
                {
                    if (o.name != "")
                        objects += o.count.ToString() + " " + o.name + (o.count > 1 ? (o.name.ToLower().EndsWith("s") ? "" : "s") : "") + "\r\n";
                }
                if (objects != "")
                    sendToUser("\r\n\r\nThe following objects are here:\r\n" + objects);
            }
            else
                sendToUser("Strange, you don't seem to be anywhere!", true, false, false);
        }

        public void doWhoRoom()
        {
            string output = "";
            foreach (Connection c in connections)
            {
                if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName != myPlayer.UserName && c.myPlayer.UserRoom == myPlayer.UserRoom)
                    output += ", " + c.myPlayer.ColourUserName;
            }
            if (output == "")
                sendToUser("You are here by yourself\r\n", true, false, false);
            else
                sendToUser("The following people are here:\r\n\r\n" + output.Substring(2) + "\r\n", true, false, false);
        }

        public void movePlayer(string room)
        {
            movePlayer(room, true);
        }

        public void movePlayer(string room, bool doLook)
        {
            movePlayer(room, doLook, true);
        }

        public void movePlayer(string room, bool doLook, bool doEnterMsg)
        {
            Room target = getRoom(room);
            if (target == null)
            {
                sendToUser("Sorry, that room is not valid", true, false, false);
                logError("Room \"" + room + "\" appears in a link from " + myPlayer.UserRoom + " but is not valid", "room");
            }
            else
            {
                Room currentRoom = Room.LoadRoom(myPlayer.UserRoom);
                if (currentRoom != null)
                    sendToRoom(myPlayer.ColourUserName + " " + myPlayer.ExitMsg, "");

                myPlayer.UserRoom = target.systemName;
                if (doEnterMsg)
                    sendToRoom(myPlayer.ColourUserName + " " + (target.enterMessage == "" ? myPlayer.EnterMsg : target.enterMessage), "", false, true);

                myPlayer.SavePlayer();
                if (doLook)
                {
                    sendToUser("\r\n", true, false, false);
                    cmdLook("");
                }
            }
        }

        public void cmdRoomMessage(string message)
        {
            if (message == "")
                sendToUser("Syntax: roommsg DEL/SHOW/<min seconds> <max seconds> <message>", true, false, false);
            else
            {
                Room currentRoom = getRoom(myPlayer.UserRoom);
                if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                {
                    if (message.ToLower() == "del")
                    {
                        currentRoom.remRoomMessage();
                        sendToUser("Room message removed", true, false, false);
                        //currentRoom.SaveRoom();
                        roomList = loadRooms();
                    }
                    else if (message.ToLower() == "show")
                    {
                        if (currentRoom.roomMessage.message == null || currentRoom.roomMessage.message == "")
                            sendToUser("No room message set", true, false, false);
                        else
                            sendToUser("Room message set to \"" + currentRoom.roomMessage.message + "\", firing every " + currentRoom.roomMessage.minTime.ToString() + (currentRoom.roomMessage.minTime == currentRoom.roomMessage.maxTime ? "" : " to " + currentRoom.roomMessage.maxTime.ToString()) + " seconds", true, false, false);
                    }
                    else
                    {
                        string[] split = message.Split(new char[] { ' ' }, 3);
                        if (split.Length < 3)
                        {
                            sendToUser("Syntax: roommsg DEL/SHOW/<min seconds> <max seconds> <message>", true, false, false);
                        }
                        else
                        {
                            int minTime;
                            int maxTime;
                            if (int.TryParse(split[0], out minTime) && int.TryParse(split[1], out maxTime))
                            {
                                if (maxTime < minTime)
                                    sendToUser("Max seconds cannot be less than Min seconds", true, false, false);
                                else
                                {
                                    currentRoom.setRoomMessage(split[2], minTime, maxTime, minTime != maxTime);
                                    //currentRoom.SaveRoom();
                                    roomList = loadRooms();
                                    sendToUser("Room message set to \"" + split[2] + "\", firing every " + minTime.ToString() + (minTime == maxTime ? "" : " to " + maxTime.ToString()) + " seconds", true, false, false);
                                }
                            }
                            else
                            {
                                sendToUser("Sorry, that is not a valid " + (!int.TryParse(split[0], out minTime) ? "min" : "max") + " seconds value", true, false, false);
                            }
                        }
                    }
                }
                else
                {
                    sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
                }
            }
        }

        public void cmdRoomEdit(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                myPlayer.InRoomEditor = true;
                editText = currentRoom.description;
                sendToUser("Entering room editor", true, false, false);
            }
            else
            {
                sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
            }
        }

        public void cmdRoomFullName(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                if (message == "")
                    sendToUser("Syntax: roomfname <full name>", true, false, false);
                else
                {
                    currentRoom.fullName = message;
                    currentRoom.SaveRoom();
                    roomList = loadRooms();
                    sendToUser("Room name set to \"" + currentRoom.fullName + "\"", true, false, false);
                }
            }
            else
            {
                sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
            }
        }

        public void cmdRoomShortName(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);

            if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                if (message == "")
                    sendToUser("Current Room short name: " + currentRoom.shortName, true, false, false);
                else if (message.IndexOf(" ") > -1)
                    sendToUser("Sorry, Room short names cannot contain spaces");
                else
                {
                    // Make sure that we append the username if it's not a system room!

                    roomList = loadRooms();


                    currentRoom.shortName = message;
                    currentRoom.SaveRoom();


                    roomList = loadRooms();
                    sendToUser("Room name set to \"" + currentRoom.systemName + "\"", true, false, false);
                }
            }
            else
            {
                sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
            }
        }

        public void cmdRoomEnterMsg(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                if (message == "")
                    sendToUser("Current room entermsg: \"" + currentRoom.enterMessage + "\"", true, false, false);
                else if (message.ToLower() == "del")
                {
                    currentRoom.enterMessage = "";
                    currentRoom.SaveRoom();
                    roomList = loadRooms();
                    sendToUser("You have removed the room entermsg", true, false, false);
                }
                else
                {
                    currentRoom.enterMessage = message;
                    currentRoom.SaveRoom();
                    roomList = loadRooms();
                    sendToUser("Room entermsg set to \"" + currentRoom.enterMessage + "\"", true, false, false);
                }
            }
            else
            {
                sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
            }
        }

        public void cmdRoomAdd(string message)
        {
            if (message == "")
                sendToUser("Syntax: roomadd " + (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? "<#>" : "") + "<room system name>", true, false, false);
            else
            {
                bool sysroom = false;
                if (message.StartsWith("#") && myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                    sysroom = true;

                if (!sysroom && roomCount() >= myPlayer.MaxRooms && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                    sendToUser("Sorry, you have reached your maximum number of rooms", true, false, false);
                else
                {
                    Room newRoom = new Room(sysroom ? message.Substring(1).ToLower() : message.ToLower(), myPlayer.UserName, sysroom);
                    newRoom.SaveRoom();
                    roomList = loadRooms();
                    sendToUser("Room \"" + newRoom.shortName + "\" (" + newRoom.systemName + ") created. Remember to link the room somewhere!", true, false, false);
                }
            }
        }

        public void cmdRoomDel(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (currentRoom.systemRoom && currentRoom.systemName.ToLower() == AppSettings.Default.DefaultLoginRoom.ToLower() && myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                sendToUser("You cannot delete the default login room!", true, false, false);
            }
            else if (currentRoom.roomOwner == myPlayer.UserName || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
            {
                // Remove all links to this room!
                for (int i = 0; i < roomList.Count; i++)
                {
                    Room temp = roomList[i];
                    for (int j = 0; j < temp.exits.Count; j++)
                    {
                        if (temp.exits[j].ToLower() == currentRoom.systemName.ToLower())
                            temp.exits[j] = "";
                    }
                    temp.SaveRoom();
                }
                string path = Path.Combine(Server.userFilePath, ("rooms" + Path.DirectorySeparatorChar + currentRoom.systemName.ToLower() + ".xml"));

                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception e)
                    {
                        Debug.Print(e.ToString());
                    }
                }

                roomList = loadRooms();
                sendToUser("Room \"" + currentRoom.shortName + "\" deleted. Moving you to main", true, false, false);
                movePlayer(AppSettings.Default.DefaultLoginRoom, false);
            }
            else
            {
                sendToUser("Sorry, you do not have permission to edit this room", true, false, false);
            }
        }

        public void cmdRoomLink(string message)
        {
            if (message == "" || message.IndexOf(" ") == -1 || !(message.ToLower().StartsWith("add") || message.ToLower().StartsWith("del")))
                sendToUser("Syntax: roomlink <add/del> <room system name>", true, false, false);
            else
            {
                Room currentRoom = getRoom(myPlayer.UserRoom);
                string[] split = message.Split(new char[] { ' ' });

                if (currentRoom.roomOwner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                    sendToUser("Sorry, you do not have permission to link this room", true, false, false);
                else if (currentRoom.systemName.ToLower() == split[1])
                    sendToUser("You cannot link a room to itself!", true, false, false);
                else
                {
                    if (split[0].ToLower() == "add")
                    {
                        roomList = loadRooms();
                        bool found = false;
                        foreach (Room r in roomList)
                        {
                            if (r.systemName.ToLower() == split[1].ToLower())
                            {
                                found = true;
                                if (r.roomOwner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                                    sendToUser("Sorry, you do not have permission to link to that room", true, false, false);
                                else
                                {
                                    bool alreadyLinked = false;
                                    foreach (string s in currentRoom.exits)
                                    {
                                        if (s.ToLower() == r.systemName.ToLower())
                                            alreadyLinked = true;
                                    }
                                    if (alreadyLinked)
                                        sendToUser("That room is already linked to this one", true, false, false);
                                    else
                                    {
                                        currentRoom.exits.Add(r.systemName);
                                        currentRoom.SaveRoom();
                                        sendToUser("You create a link from this room to \"" + r.systemName + "\" (Don't forget to link it back the other way for two way access)", true, false, false);
                                    }
                                }
                            }
                        }
                        if (!found)
                        {
                            sendToUser("Room \"" + split[1] + "\" not found", true, false, false);
                        }
                    }
                    else
                    {
                        string remove = "";
                        foreach (string s in currentRoom.exits)
                        {
                            if (s.ToLower() == split[1].ToLower())
                                remove = s;
                        }
                        if (remove == "")
                            sendToUser("Link \"" + split[1] + "\" not found", true, false, false);
                        else
                        {
                            currentRoom.exits.Remove(remove);
                            currentRoom.SaveRoom();
                            roomList = loadRooms();
                            sendToUser("You delete the link from this room to \"" + remove + "\"", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdRoomList(string message)
        {
            string output = "\r\n{red}" + "System Name".PadRight(20) + "Short Name".PadRight(20) + "Full Name".PadRight(20) + "Owner{reset}\r\n";
            if (message == "")
            {
                // List all the rooms
                roomList = loadRooms();
                int roomCount = 0;
                foreach (Room r in roomList)
                {
                    output += (r.systemName.Length >= 20 ? r.systemName.Substring(0, 16) + "..." : r.systemName).PadRight(20)
                        + (r.shortName.Length >= 20 ? r.shortName.Substring(0, 16) + "..." : r.shortName).PadRight(20)
                        + (r.fullName.Length >= 20 ? r.fullName.Substring(0, 16) + "..." : r.fullName).PadRight(20)
                        + (r.roomOwner.Length >= 20 ? r.roomOwner.Substring(0, 16) + "..." : r.roomOwner) + "\r\n";
                    roomCount++;
                }
                sendToUser(headerLine("All Rooms") + "\r\nThere " + (roomCount != 1 ? "are " + roomCount.ToString() + " rooms" : "is one room") + ":" + output + footerLine(), true, false, false);
            }
            else
            {
                roomList = loadRooms();
                int roomCount = 0;
                foreach (Room r in roomList)
                {
                    if (r.roomOwner.ToLower().StartsWith(message.ToLower()))
                    {
                        output += (r.systemName.Length >= 20 ? r.systemName.Substring(0, 16) + "..." : r.systemName).PadRight(20)
                        + (r.shortName.Length >= 20 ? r.shortName.Substring(0, 16) + "..." : r.shortName).PadRight(20)
                        + (r.fullName.Length >= 20 ? r.fullName.Substring(0, 16) + "..." : r.fullName).PadRight(20)
                        + (r.roomOwner.Length >= 20 ? r.roomOwner.Substring(0, 16) + "..." : r.roomOwner) + "\r\n";
                        roomCount++;
                    }
                }
                sendToUser(headerLine("Rooms matching \"" + message + "\"") + "\r\nThere " + (roomCount != 1 ? "are " + roomCount.ToString() + " rooms" : "is one room") + ":" + output + footerLine(), true, false, false);
            }
        }

        public void cmdRoomLock(string message)
        {
            Room currentRoom = getRoom(myPlayer.UserRoom);
            if (message == "")
            {
                if (currentRoom.roomOwner.ToLower() == myPlayer.UserName.ToLower() || myPlayer.PlayerRank > (int)Player.Rank.Staff)
                {
                    string output = headerLine("Current room locks for " + currentRoom.shortName) + "\r\n";
                    output += "Full lock:".PadRight(15) + (currentRoom.locks.FullLock ? "Yes" : "No") + "\r\n";
                    if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                    {
                        output += "Admin lock:".PadRight(15) + (currentRoom.locks.AdminLock ? "Yes" : "No") + "\r\n";
                        output += "Staff lock:".PadRight(15) + (currentRoom.locks.StaffLock ? "Yes" : "No") + "\r\n";
                        output += "Guide lock:".PadRight(15) + (currentRoom.locks.GuideLock ? "Yes" : "No") + "\r\n";
                    }
                    output += "Friends lock:".PadRight(15) + (currentRoom.locks.FriendLock ? "Yes" : "No") + "\r\n";
                    output += footerLine();
                    sendToUser(output, true, false, false);
                }
                else
                    sendToUser("You are not authorised to see the locks on this room", true, false, false);
            }
            else
            {
                if (currentRoom.roomOwner.ToLower() != myPlayer.UserName.ToLower() && myPlayer.PlayerRank < (int)Player.Rank.Admin)
                    sendToUser("Sorry, you do not have permission to change the locks in this room", true, false, false);
                else
                {
                    switch (message.ToLower())
                    {
                        case "full":
                            currentRoom.locks.FullLock = !currentRoom.locks.FullLock;
                            sendToUser("You " + (currentRoom.locks.FullLock ? "add" : "remove") + " the full room lock", true, false, false);
                            break;
                        case "admin":
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                currentRoom.locks.AdminLock = !currentRoom.locks.AdminLock;
                                sendToUser("You " + (currentRoom.locks.AdminLock ? "add" : "remove") + " the admin only lock", true, false, false);
                            }
                            else
                            {
                                sendToUser("Syntax: roomlock <full/friends/none>", true, false, false);
                            }
                            break;
                        case "staff":
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                currentRoom.locks.StaffLock = !currentRoom.locks.StaffLock;
                                sendToUser("You " + (currentRoom.locks.StaffLock ? "add" : "remove") + " the staff only lock", true, false, false);
                            }
                            else
                            {
                                sendToUser("Syntax: roomlock <full/friends/none>", true, false, false);
                            }
                            break;
                        case "guide":
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                currentRoom.locks.GuideLock = !currentRoom.locks.GuideLock;
                                sendToUser("You " + (currentRoom.locks.GuideLock ? "add" : "remove") + " the guide only lock", true, false, false);
                            }
                            else
                            {
                                sendToUser("Syntax: roomlock <full/friends/none>", true, false, false);
                            }
                            break;
                        case "friend":
                            currentRoom.locks.FriendLock = !currentRoom.locks.FriendLock;
                            sendToUser("You " + (currentRoom.locks.FriendLock ? "add" : "remove") + " the friends only lock", true, false, false);
                            break;
                        case "all":
                            currentRoom.locks.FullLock = true;
                            currentRoom.locks.FriendLock = true;
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                currentRoom.locks.AdminLock = true;
                                currentRoom.locks.StaffLock = true;
                                currentRoom.locks.GuideLock = true;
                            }
                            sendToUser("You add all the locks", true, false, false);
                            break;
                        case "none":
                            currentRoom.locks.FullLock = false;
                            currentRoom.locks.FriendLock = false;
                            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                currentRoom.locks.AdminLock = false;
                                currentRoom.locks.StaffLock = false;
                                currentRoom.locks.GuideLock = false;
                            }
                            sendToUser("You remove all the locks", true, false, false);
                            break;
                        default:
                            sendToUser("Syntax: roomlock <full/" + (myPlayer.PlayerRank >= (int)Player.Rank.Admin ? "admin/staff/guide/all/" : "") + "friends/none>", true, false, false);
                            break;
                    }
                    currentRoom.SaveRoom();
                    roomList = loadRooms();
                }
            }
        }

        public void cmdBarge(string message)
        {
            if (message == "")
                sendToUser("Syntax: barge <username>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                else if (target[0] == myPlayer.UserName)
                    sendToUser("Trying to barge in on yourself?", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0])
                        {
                            if (c.myPlayer.PlayerRank < myPlayer.PlayerRank)
                            {
                                movePlayer(c.myPlayer.UserRoom, false);
                                sendToUser("You have barged in on " + c.myPlayer.UserName, true, false, false);
                            }
                            else
                                sendToUser("I can't let you do that, Dave ....", true, false, false);
                        }
                    }
                }
            }
        }

        public void cmdGrab(string message)
        {
            if (message == "")
                sendToUser("Syntax: grab <username>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online", true, false, false);
                else if (target[0] == myPlayer.UserName)
                    sendToUser("Trying to grab yourself?", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            if (c.myPlayer.CanGrabMe(myPlayer.UserName) || myPlayer.PlayerRank >= (int)Player.Rank.Admin)
                            {
                                if (c.myPlayer.UserRoom.ToLower() == myPlayer.UserRoom.ToLower())
                                    sendToUser("You are already in the same room as " + c.myPlayer.ColourUserName, true, false, false);
                                else
                                {
                                    sendToUser("You grab " + c.myPlayer.ColourUserName + " and drop them next to you", true, false, false);
                                    c.sendToUser("\r\nAn etherial hand reaches down, lifts you up and drops you next to " + myPlayer.ColourUserName, true, true, false);
                                    c.movePlayer(myPlayer.UserRoom, false, false);
                                }
                            }
                            else
                            {
                                sendToUser("You can't seem to get a grasp on " + c.myPlayer.ColourUserName, true, false, false);
                            }
                        }
                    }
                }
            }
        }

        public int roomCount()
        {
            roomList = loadRooms();
            int ret = 0;
            foreach (Room r in roomList)
            {
                if (r.roomOwner.ToLower() == myPlayer.UserName.ToLower())
                    ret++;
            }
            return ret;
        }

        public void cmdTrans(string message)
        {
            string[] split = message.Split(new char[] { ' ' });
            if (message == "" || split.Length != 2)
                sendToUser("Syntax: trans <player> <room>", true, false, false);
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
                    Room targRoom = getRoom(split[1]);
                    if (targRoom != null)
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                            {
                                if (c.myPlayer.UserRoom.ToLower() == targRoom.systemName.ToLower())
                                    sendToUser((c.myPlayer.UserName == myPlayer.UserName ? "You are " : c.myPlayer.UserName + " is ") + "already there!", true, false, false);
                                else
                                {
                                    c.movePlayer(targRoom.systemName);
                                    sendToUser("As you wish ...", true, false, false);
                                }
                                return;
                            }
                        }
                        sendToUser("Strange - something wierd has happened!", true, false, false);
                    }
                    else
                        sendToUser("Room \"" + split[1] + "\" not found", true, false, false);
                }
            }

        }

        #region Rooms

        private string getRoomFullName(string room)
        {
            string ret = "";
            foreach (Room r in roomList)
            {
                if (r.systemName == room)
                    ret = r.fullName;
            }
            return ret;
        }

        private Room getRoom(string room)
        {
            roomList = loadRooms();
            Room ret = null;
            foreach (Room r in roomList)
            {
                if (r.systemName.ToLower() == room.ToLower())
                    ret = r;
            }
            return ret;
        }

        #endregion

        public void roomEdit(string message)
        {
            if (message.StartsWith("."))
            {
                switch (message)
                {
                    case ".end":
                    case ".":
                        myPlayer.InRoomEditor = false;
                        Room temp = Room.LoadRoom(myPlayer.UserRoom);
                        temp.description = editText;
                        temp.SaveRoom();
                        roomList = loadRooms();
                        sendToUser("Description set", true, false, false);
                        editText = "";
                        break;
                    case ".wipe":
                        editText = "";
                        break;
                    case ".view":
                        sendToUser(editText, true, false, false);
                        break;
                    case ".quit":
                        editText = "";
                        myPlayer.InRoomEditor = false;
                        sendToUser("Description edit aborted", true, false, false);
                        break;
                    default:
                        sendToUser("Commands available:\r\n.view - show current description content\r\n.wipe - wipe current description content\r\n.quit - exit the editor without saving desctiption\r\n.end - exit the editor and save description", true, false, false);
                        break;
                }
            }
            else
            {
                editText += message + "\r\n";
            }
            doPrompt();
        }


        #endregion

    }
}
