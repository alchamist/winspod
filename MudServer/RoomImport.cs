using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MudServer
{
    // Bulk room-layout creation for admins, without hand-running roomadd/roomfname/
    // roomedit/roomlink one room at a time. Deliberately takes its input the same way
    // the existing mail/room-description editors do (RoomCommands.cs's roomEdit,
    // Mail.cs's mailEdit): a capture mode entered via a command, ended by a "." line,
    // fed straight from whatever's pasted into the admin's already-authenticated
    // telnet session - not a file upload, not anything checked into source control, so
    // a server operator's specific room layout never has to leave their own session.
    //
    // The pasted text is parsed as JSON into the fixed DTOs below via System.Text.Json,
    // which - unlike XmlSerializer or BinaryFormatter - has no way to specify "construct
    // an arbitrary .NET type"; it can only ever populate the exact properties declared
    // here. Every room it creates goes through the same Room constructor and the same
    // \W-only shortName validation cmdRoomAdd already enforces (RoomCommands.cs), so a
    // malformed or hostile entry can at worst be rejected per-room, not write outside
    // the rooms directory.
    public partial class Connection
    {
        private string roomImportText = "";
        private const int MaxRoomImportLength = 200_000;

        public void cmdRoomImport(string message)
        {
            if (myPlayer.PlayerRank < (int)Player.Rank.Admin)
            {
                sendToUser("Sorry, you do not have permission to do that", true, false, false);
                return;
            }

            myPlayer.InRoomImport = true;
            roomImportText = "";
            sendToUser("Entering room import mode. Paste your JSON room definition, then " +
                "type \".end\" on its own line to apply it.\r\n" +
                "(\".quit\" aborts, \".view\" shows what's captured so far, \".wipe\" clears it)",
                true, false, false);
        }

        public void roomImport(string message)
        {
            if (message.StartsWith("."))
            {
                switch (message)
                {
                    case ".end":
                    case ".":
                        myPlayer.InRoomImport = false;
                        ApplyRoomImport(roomImportText);
                        roomImportText = "";
                        break;
                    case ".wipe":
                        roomImportText = "";
                        sendToUser("Import buffer cleared", true, false, false);
                        break;
                    case ".view":
                        sendToUser(roomImportText == "" ? "(nothing captured yet)" : roomImportText, true, false, false);
                        break;
                    case ".quit":
                        roomImportText = "";
                        myPlayer.InRoomImport = false;
                        sendToUser("Room import aborted", true, false, false);
                        break;
                    default:
                        sendToUser("Commands available:\r\n.view - show captured text so far\r\n.wipe - wipe captured text\r\n.quit - abort without applying\r\n.end - apply the captured JSON", true, false, false);
                        break;
                }
            }
            else
            {
                roomImportText += message + "\r\n";
                if (roomImportText.Length > MaxRoomImportLength)
                {
                    sendToUser("Sorry, that's too much text - aborting the import", true, false, false);
                    roomImportText = "";
                    myPlayer.InRoomImport = false;
                }
            }
            doPrompt();
        }

        private void ApplyRoomImport(string json)
        {
            RoomImportFile import;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                import = JsonSerializer.Deserialize<RoomImportFile>(json, options);
            }
            catch (JsonException ex)
            {
                sendToUser("Sorry, that wasn't valid JSON: " + ex.Message, true, false, false);
                return;
            }

            if (import == null || import.Rooms == null || import.Rooms.Count == 0)
            {
                sendToUser("No rooms found in that import", true, false, false);
                return;
            }

            List<string> errors = new List<string>();
            List<string> applied = new List<string>();
            Dictionary<string, string> keyToSystemName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string dup in import.Rooms
                .Where(r => !string.IsNullOrWhiteSpace(r.Key))
                .GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key))
            {
                errors.Add("Key \"" + dup + "\" is used more than once - only the last entry with that key will resolve for exits");
            }

            // Pass 1: create/update each room's own fields.
            int defaultRoomCount = 0;
            foreach (RoomImportEntry entry in import.Rooms)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    errors.Add("A room entry is missing a \"key\" - skipped");
                    continue;
                }

                if (entry.ReplaceDefaultRoom)
                {
                    defaultRoomCount++;
                    if (defaultRoomCount > 1)
                    {
                        errors.Add("\"" + entry.Key + "\": more than one entry sets replaceDefaultRoom - skipped");
                        continue;
                    }

                    Room defaultRoom = Room.LoadRoom(AppSettings.Default.DefaultLoginRoom);
                    if (defaultRoom == null || defaultRoom.fullName == null)
                    {
                        errors.Add("\"" + entry.Key + "\": could not load the current default room (" + AppSettings.Default.DefaultLoginRoom + ") - skipped");
                        continue;
                    }

                    ApplyRoomFields(defaultRoom, entry);
                    defaultRoom.SaveRoom();
                    keyToSystemName[entry.Key] = defaultRoom.systemName;
                    applied.Add(entry.Key + " -> " + defaultRoom.systemName + " (default room)");
                }
                else
                {
                    if (string.IsNullOrEmpty(entry.ShortName) || Regex.Replace(entry.ShortName, @"\W*", "") != entry.ShortName)
                    {
                        errors.Add("\"" + entry.Key + "\": shortName must be alphanumeric with no spaces - skipped");
                        continue;
                    }

                    Room room = new Room(entry.ShortName.ToLower(), myPlayer.UserName, true);
                    ApplyRoomFields(room, entry);
                    room.SaveRoom();
                    keyToSystemName[entry.Key] = room.systemName;
                    applied.Add(entry.Key + " -> " + room.systemName);
                }
            }

            roomList = loadRooms();

            // Pass 2: resolve and link exits now that every room in this import exists,
            // so entries can reference each other regardless of the order they appear in.
            foreach (RoomImportEntry entry in import.Rooms)
            {
                if (entry.Exits == null || entry.Exits.Count == 0 || entry.Key == null ||
                    !keyToSystemName.TryGetValue(entry.Key, out string ownSystemName))
                    continue;

                Room room = getRoom(ownSystemName);
                if (room == null)
                    continue;

                foreach (KeyValuePair<string, string> exit in entry.Exits)
                {
                    string targetSystemName = null;
                    if (keyToSystemName.TryGetValue(exit.Value ?? "", out string resolved))
                        targetSystemName = resolved;
                    else
                    {
                        Room existing = getRoom(exit.Value ?? "");
                        if (existing != null)
                            targetSystemName = existing.systemName;
                    }

                    if (targetSystemName == null)
                    {
                        errors.Add("\"" + entry.Key + "\" exit \"" + exit.Key + "\": target \"" + exit.Value + "\" not found - skipped");
                        continue;
                    }

                    if (!room.exits.Any(e => e.ToLower() == targetSystemName.ToLower()))
                        room.exits.Add(targetSystemName);
                }

                room.SaveRoom();
            }

            roomList = loadRooms();

            string summary = "Room import complete.\r\n";
            if (applied.Count > 0)
                summary += "Created/updated: " + string.Join(", ", applied) + "\r\n";
            if (errors.Count > 0)
                summary += "Issues:\r\n - " + string.Join("\r\n - ", errors);

            sendToUser(summary, true, false, false);
        }

        private void ApplyRoomFields(Room room, RoomImportEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.FullName))
                room.fullName = entry.FullName;
            if (entry.Description != null)
                room.description = entry.Description;
            if (entry.EnterMessage != null)
                room.enterMessage = entry.EnterMessage;

            if (entry.Locks != null)
            {
                if (entry.Locks.Full.HasValue) room.locks.FullLock = entry.Locks.Full.Value;
                if (entry.Locks.Friend.HasValue) room.locks.FriendLock = entry.Locks.Friend.Value;
                if (entry.Locks.Staff.HasValue) room.locks.StaffLock = entry.Locks.Staff.Value;
                if (entry.Locks.Admin.HasValue) room.locks.AdminLock = entry.Locks.Admin.Value;
                if (entry.Locks.Guide.HasValue) room.locks.GuideLock = entry.Locks.Guide.Value;
            }

            if (entry.RoomMessage != null && !string.IsNullOrEmpty(entry.RoomMessage.Text))
            {
                room.setRoomMessage(entry.RoomMessage.Text, entry.RoomMessage.MinSeconds,
                    entry.RoomMessage.MaxSeconds, entry.RoomMessage.MinSeconds != entry.RoomMessage.MaxSeconds);
            }
        }
    }

    public class RoomImportFile
    {
        public List<RoomImportEntry> Rooms { get; set; }
    }

    public class RoomImportEntry
    {
        public string Key { get; set; }
        public bool ReplaceDefaultRoom { get; set; }
        public string ShortName { get; set; }
        public string FullName { get; set; }
        public string Description { get; set; }
        public string EnterMessage { get; set; }
        public Dictionary<string, string> Exits { get; set; }
        public RoomImportLocks Locks { get; set; }
        public RoomImportMessage RoomMessage { get; set; }
    }

    public class RoomImportLocks
    {
        public bool? Full { get; set; }
        public bool? Friend { get; set; }
        public bool? Staff { get; set; }
        public bool? Admin { get; set; }
        public bool? Guide { get; set; }
    }

    public class RoomImportMessage
    {
        public string Text { get; set; }
        public int MinSeconds { get; set; }
        public int MaxSeconds { get; set; }
    }
}
