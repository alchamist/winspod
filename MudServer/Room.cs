﻿using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace MudServer
{
    public class Room
    {
        public enum Direction
        {
            North,
            NorthNorthEast,
            NorthEast,
            EastNorthEast,
            East,
            EastSouthEast,
            SouthEast,
            SouthSouthEast,
            South,
            SouthSouthWest,
            SouthWest,
            WestSouthWest,
            West,
            WestNorthWest,
            NorthWest,
            NorthNorthWest,
            Up,
            Down
        }


        public struct roomLocks
        {
            public bool             FullLock;
            public bool             FriendLock;
            public bool             StaffLock;
            public bool             AdminLock;
            public bool             GuideLock;
        }

        public struct roomMessages
        {
            public string           message;  // Message to send to the room
            public int              minTime;     // Minimum or normal time between messages (in seconds)
            public int              maxTime;     // Max time between messages if random (in seconds)
            public bool             isRandom; // Is this a random or fixed interval message?
            public DateTime         nextFire; // Timestamp the message will be fired next
        }

        public struct roomObjects
        {
            public string           name;   // Name of the object in the room
            public int              count;  // How many of the object is there?
        }

        
        public string               systemName;
        public string               shortName;
        public string               fullName;
        public string               enterMessage = "";
        public string               description = "";
        public List<string>         exits = new List<string>();
        public bool                 systemRoom = false;
        public roomLocks            locks;
        public string               roomOwner = null;
        public roomMessages         roomMessage;
        public List<roomObjects>    roomContents = new List<roomObjects>(); // List of objects in a room

        public Room()
        {
            
        }

        public Room(string shrtName, string owner, bool sysRoom)
        {
            systemName = owner.ToLower() + "." + shrtName.ToLower();
            shortName = shrtName;
            fullName = "Undefined Room Name";
            description = "A boring room with no description";
            systemRoom = sysRoom;
            roomOwner = sysRoom ? "System" : owner;
        }

        public static Room LoadRoom(string roomName)
        {
            Room load;
            string path = Path.Combine(Server.userFilePath,("rooms" + Path.DirectorySeparatorChar + roomName.ToLower() + ".xml"));

            if (File.Exists(path))
            {
                try
                {
                    XmlSerializer deserial = new XmlSerializer(typeof(Room));
                    TextReader textReader = new StreamReader(@path);
                    load = (Room)deserial.Deserialize(textReader);
                    textReader.Close();
                }
                catch (Exception e)
                {
                    Debug.Print(e.ToString());
                    load = null;
                }
            }
            else
            {
                load = new Room();
            }

            for (int i = load.exits.Count-1; i >= 0; i--)
            {
                if (load.exits[i] == "" || load.exits[i] == null)
                    load.exits.RemoveAt(i);
            }

            return load;
        }

        public void SaveRoom()
        {
            if (this.systemName != null && this.fullName != null)
            {
                string path = Path.Combine(Server.userFilePath,@"rooms" + Path.DirectorySeparatorChar);
                string fname = this.systemName + ".xml";
                string fpath = path + fname;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                XmlSerializer serial = new XmlSerializer(typeof(Room));
                TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                serial.Serialize(textWriter, this);
                textWriter.Close();
            }
        }

        public void remRoomMessage()
        {
            roomMessage = new roomMessages();
            SaveRoom();
        }

        public void setRoomMessage(string message, int minTime, int maxTime, bool isRandom)
        {
            roomMessages rm = new roomMessages();
            rm.message = message;
            rm.minTime = minTime;
            rm.maxTime = maxTime;
            rm.isRandom = isRandom;
            if (isRandom)
                rm.nextFire = DateTime.Now.AddSeconds(new Random().Next(minTime, maxTime));
            else
                rm.nextFire = DateTime.Now.AddSeconds((double)minTime);

            roomMessage = rm;
            SaveRoom();
        }

        public string timerFire()
        {
            string ret = "";
            if (roomMessage.message != null && roomMessage.message != "" && roomMessage.minTime > 0)
            {
                if (DateTime.Now >= roomMessage.nextFire)
                {
                    ret = roomMessage.message;
                    if (roomMessage.isRandom)
                        roomMessage.nextFire = DateTime.Now.AddSeconds(new Random().Next(roomMessage.minTime, roomMessage.maxTime));
                    else
                        roomMessage.nextFire = DateTime.Now.AddSeconds((double)roomMessage.minTime);
                }
            }
            return ret;
        }

        public void addObject(string name)
        {
            roomObjects temp = new roomObjects();

            for (int i = 0; i < roomContents.Count; i++)
            {
                if (roomContents[i].name.ToLower() == name.ToLower())
                {
                    temp = roomContents[i];
                    temp.count++;
                    roomContents[i] = temp;
                    SaveRoom();
                    return;
                }
            }

            temp.name = name;
            temp.count = 1;
            roomContents.Add(temp);
            SaveRoom();
        }

        public void removeObject(string name)
        {
            for (int i = roomContents.Count-1; i >= 0; i--)
            {
                if (roomContents[i].name.ToLower() == name.ToLower())
                {
                    if (roomContents[i].count == 1)
                        roomContents.RemoveAt(i);
                    else
                    {
                        roomObjects temp = roomContents[i];
                        temp.count--;
                        roomContents[i] = temp;
                        SaveRoom();
                        return;
                    }
                }
            }
        }

        public void removeAllObjects()
        {
            this.roomContents = new List<roomObjects>();
            SaveRoom();
        }

        public int isObjectInRoom(string name)
        {
            foreach (roomObjects r in roomContents)
            {
                if (r.name.ToLower() == name.ToLower())
                {
                    return r.count;
                }
            }
            return 0;
        }
    }
}
