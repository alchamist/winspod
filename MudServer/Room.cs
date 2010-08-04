using System;
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
            public bool FullLock;
            public bool FriendLock;
            public bool StaffLock;
            public bool AdminLock;
            public bool GuideLock;
        }


        public string       shortName;
        public string       fullName;
        public string       enterMessage = "";
        public string       description = "";
        public string[]     exits = new string[18];
        public bool         systemRoom = false;
        public roomLocks    locks;
        public string       roomOwner = null;

        public Room()
        {
            for (int i = 0; i < exits.Length; i++)
                exits[i] = "";
        }

        public static Room LoadRoom(string roomName)
        {
            Room load;
            string path = ("rooms" + Path.DirectorySeparatorChar + roomName.ToLower() + ".xml");

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
            return load;
        }

        public void SaveRoom()
        {
            if (this.shortName != null && this.fullName != null)
            {
                string path = @"rooms" + Path.DirectorySeparatorChar;
                string fname = this.shortName + ".xml";
                string fpath = path + fname;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                XmlSerializer serial = new XmlSerializer(typeof(Room));
                TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                serial.Serialize(textWriter, this);
                textWriter.Close();
            }
        }


    }
}
