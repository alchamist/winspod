using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace MudServer
{
    public class ClubChannel
    {
        private int         channelNumber = 0;                          // The channel number
        private string      name;                                       // The channel name
        private string      description = "A dull channel description"; // The channel description
        private string      owner;                                      // The channel owner (player name)
        private string      mainColour = "{bold}{red}";                 // The colour the main text is sent in
        private string      prefixColour = "{bold}{cyan}";              // The colour the []'s around the name are in
        private string      nameColour = "{bold}{magenta}";             // The colour the name of the channel is within the []'s

        private List<string> players = new List<string>();              // A list of player names on the channel

        #region Getters and Setters

        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        public int ID
        {
            get { return channelNumber; }
            set { channelNumber = value; }
        }

        public string Description
        {
            get { return description; }
            set { description = value; }
        }

        public string Owner
        {
            get { return owner; }
            set { owner = value; }
        }

        public string MainColour
        {
            get { return mainColour; }
            set { mainColour = value; }
        }

        public string PreColour
        {
            get { return prefixColour; }
            set { prefixColour = value; }
        }

        public string NameColour
        {
            get { return nameColour; }
            set { nameColour = value; }
        }

        public List<string> Users
        {
            get { return players; }
        }

#endregion

        #region Class methods

        public void AddPlayer(string playerName)
        {
            if (players.IndexOf(playerName) == -1)
            {
                players.Add(playerName);
            }
        }

        public void RemovePlayer(string playerName)
        {
            if (players.IndexOf(playerName) > -1)
            {
                players.RemoveAt(players.IndexOf(playerName));
            }
        }

        public void Delete()
        {
            string path = ("channels" + Path.DirectorySeparatorChar);

            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                FileInfo[] fi = di.GetFiles();
                foreach (FileInfo f in fi)
                {
                    if (f.Name.Replace(f.Extension, "") == channelNumber.ToString())
                    {
                        f.Delete();
                    }
                }
            }
        }

        public bool SaveChannel()
        {
            if (this.ID != 0)
            {
                bool ret = false;
                try
                {
                    string path = Path.Combine(Server.userFilePath,@"channels" + Path.DirectorySeparatorChar);
                    string fname = this.ID.ToString() + ".xml";
                    string fpath = path + fname;
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);

                    XmlSerializer serial = new XmlSerializer(typeof(ClubChannel));
                    TextWriter textWriter = new StreamWriter(@fpath.ToLower());
                    serial.Serialize(textWriter, this);
                    textWriter.Close();
                    ret = true;
                }
                catch (Exception ex)
                {
                    Connection.logError(ex.ToString(), "filesystem");
                }
                return ret;
            }
            else
                return false;
        }

        public bool OnChannel(string playerName)
        {
            return (players.IndexOf(playerName) > -1 || playerName.ToLower() == owner.ToLower());
        }

        public string FormatMessage(string message)
        {
            return prefixColour + "[" + nameColour + name + prefixColour + "] " + mainColour + message + "{reset}";
        }

        #endregion

        #region Static Methods

        public static List<ClubChannel> GetChannels()
        {
            List<ClubChannel> ret = new List<ClubChannel>();
            string path = Path.Combine(Server.userFilePath,("channels" + Path.DirectorySeparatorChar));

            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                FileInfo[] fi = di.GetFiles();
                foreach (FileInfo f in fi)
                {
                    ClubChannel load;
                    try
                    {
                        XmlSerializer deserial = new XmlSerializer(typeof(ClubChannel));
                        TextReader textReader = new StreamReader(@path + f.Name);
                        load = (ClubChannel)deserial.Deserialize(textReader);
                        textReader.Close();
                        ret.Add(load);
                    }
                    catch (Exception e)
                    {
                        Debug.Print(e.ToString());
                    }
                }
            }
            return ret;
        }

        public static ClubChannel LoadChannel(string name)
        {
            ClubChannel channel = null;
            string path = Path.Combine(Server.userFilePath,("channels" + Path.DirectorySeparatorChar));

            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                FileInfo[] fi = di.GetFiles();
                foreach (FileInfo f in fi)
                {
                    ClubChannel load;
                    try
                    {
                        XmlSerializer deserial = new XmlSerializer(typeof(ClubChannel));
                        TextReader textReader = new StreamReader(@path + f.Name);
                        load = (ClubChannel)deserial.Deserialize(textReader);
                        textReader.Close();
                        if (load.Name.ToLower() == name.ToLower())
                            channel = load;
                    }
                    catch (Exception e)
                    {
                        Debug.Print(e.ToString());
                    }
                }
            }

            return channel;
        }

        public static ClubChannel LoadChannel(int channelNum)
        {
            ClubChannel channel = null;
            string path = Path.Combine(Server.userFilePath,("channels" + Path.DirectorySeparatorChar));

            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                FileInfo[] fi = di.GetFiles();
                foreach (FileInfo f in fi)
                {
                    if (f.Name.Replace(f.Extension, "") == channelNum.ToString())
                    {
                        ClubChannel load;
                        try
                        {
                            XmlSerializer deserial = new XmlSerializer(typeof(ClubChannel));
                            TextReader textReader = new StreamReader(@path + f.Name);
                            load = (ClubChannel)deserial.Deserialize(textReader);
                            textReader.Close();
                            channel = load;
                        }
                        catch (Exception e)
                        {
                            Debug.Print(e.ToString());
                        }
                    }
                }
            }

            return channel;
        }

        public static List<ClubChannel> LoadAllChannels()
        {
            List<ClubChannel> channels = new List<ClubChannel>();
            string path = Path.Combine(Server.userFilePath,("channels" + Path.DirectorySeparatorChar));

            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                FileInfo[] fi = di.GetFiles();
                foreach (FileInfo f in fi)
                {
                    ClubChannel load;
                    try
                    {
                        XmlSerializer deserial = new XmlSerializer(typeof(ClubChannel));
                        TextReader textReader = new StreamReader(@path + f.Name);
                        load = (ClubChannel)deserial.Deserialize(textReader);
                        textReader.Close();
                        channels.Add(load);
                    }
                    catch (Exception e)
                    {
                        Debug.Print(e.ToString());
                    }
                }
            }

            return SortChannels(channels);
        }

        public static List<ClubChannel> SortChannels(List<ClubChannel> channels)
        {
            channels.Sort(delegate(ClubChannel p1, ClubChannel p2) { return p1.channelNumber.CompareTo(p2.channelNumber); });
            return channels;
        }

        public static List<ClubChannel> ReindexChannels(List<ClubChannel> channels)
        {
            //List<ClubChannel> ret = SortChannels(channels);
            List<ClubChannel>ret = channels;
            int count = 1;
            foreach (ClubChannel c in ret)
            {
                c.ID = count++;
            }
            return ret;
        }

        public static void SaveAllChannels(List<ClubChannel> channels, bool reindexChannels)
        {
            if (reindexChannels)
                channels = ReindexChannels(channels);

            string path = Path.Combine(Server.userFilePath,("channels" + Path.DirectorySeparatorChar));

            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                FileInfo[] fi = di.GetFiles();
                foreach (FileInfo f in fi)
                {
                    f.Delete();
                }
            }
            else
            {
                Directory.CreateDirectory(path);
            }
            foreach (ClubChannel c in channels)
            {
                c.SaveChannel();
            }
        }

        #endregion
    }
}
