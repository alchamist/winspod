using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MudServer
{
    public partial class Connection
    {

        public void cmdSet(string message)
        {
            string syntax = "Syntax: set <info> <value>\r\n" + footerLine() + "\r\n^YAvailable info settings:^N\r\n\r\n jabber, icq, msn, yahoo, skype, email, hUrl, wUrl, irl, occ, home, jetlag, favourite, facebook, twitter\r\n" + footerLine();
            if (message == "")
                sendToUser(syntax, true, false, false);
            else
            {
                // Split the input to see if we are setting or blanking
                string[] split = message.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                switch (split[0].ToLower())
                {
                    case "jabber":
                        myPlayer.JabberAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Jabber id to " + split[1] : "You blank your Jabber id", true, false, false);
                        break;
                    case "icq":
                        myPlayer.ICQAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your ICQ id to " + split[1] : "You blank your ICQ id", true, false, false);
                        break;
                    case "msn":
                        myPlayer.MSNAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your MSN id to " + split[1] : "You blank your MSN id", true, false, false);
                        break;
                    case "yahoo":
                        myPlayer.YahooAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Yahoo id to " + split[1] : "You blank your Yahoo id", true, false, false);
                        break;
                    case "skype":
                        myPlayer.SkypeAddress = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Skype id to " + split[1] : "You blank your Skype id", true, false, false);
                        break;
                    case "email":
                        if (split.Length == 1)
                            sendToUser("Your e-mail address visibility is set to " + (Player.ShowTo)myPlayer.EmailPermissions, true, false, false);
                        else if (split[1] == "public")
                        {
                            myPlayer.EmailPermissions = (int)Player.ShowTo.Public;
                            sendToUser("You set your e-mail visible to everyone", true, false, false);
                        }
                        else if (split[1] == "private")
                        {
                            myPlayer.EmailPermissions = (int)Player.ShowTo.Private;
                            sendToUser("You set your e-mail visible to only admin", true, false, false);
                        }
                        else if (split[1] == "friends")
                        {
                            myPlayer.EmailPermissions = (int)Player.ShowTo.Friends;
                            sendToUser("You set your e-mail visible to your friends", true, false, false);
                        }
                        else
                        {
                            if (testEmailRegex(split[1]))
                            {
                                myPlayer.EmailAddress = split[1];
                                sendToUser("You set your Email address to " + split[1], true, false, false);
                            }
                            else
                                sendToUser("Please enter a valid e-mail address", true, false, false);
                        }
                        break;
                    case "hurl":
                        myPlayer.HomeURL = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Home URL to " + split[1] : "You blank your Home URL", true, false, false);
                        break;
                    case "wurl":
                        myPlayer.WorkURL = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Work URL to " + split[1] : "You blank your Work URL", true, false, false);
                        break;
                    case "irl":
                        myPlayer.RealName = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your real name to " + split[1] : "You blank your real name", true, false, false);
                        break;
                    case "occ":
                        myPlayer.Occupation = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your occupation to " + split[1] : "You blank your occupation URL", true, false, false);
                        break;
                    case "home":
                        myPlayer.Hometown = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Home Town to " + split[1] : "You blank your Home Town", true, false, false);
                        break;
                    case "jetlag":
                        int jetlag = split.Length > 1 ? Convert.ToInt32(split[1]) : 0;
                        if (jetlag >= -12 && jetlag <= 12)
                        {
                            myPlayer.JetLag = jetlag;
                            sendToUser("You set your jetlag to " + jetlag.ToString(), true, false, false);
                        }
                        else
                            sendToUser("Maximum jetlag is +/- 12 hours", true, false, false);
                        break;
                    case "facebook":
                        if (split.Length > 1 && split[1].ToLower().IndexOf("facebook.com") == -1)
                            sendToUser("Sorry, that is not a facebook url!", true, false, false);
                        else
                        {
                            myPlayer.FacebookPage = (split.Length > 1 ? split[1] : "");
                            sendToUser(split.Length > 1 ? "You set your Facebook page to " + split[1] : "You blank your Facebook Page", true, false, false);
                        }
                        break;
                    case "twitter":
                        myPlayer.Twitter = (split.Length > 1 ? split[1] : "");
                        sendToUser(split.Length > 1 ? "You set your Twitter ID to " + split[1] : "You blank your Twitter ID", true, false, false);
                        break;
                    case "favourite":
                        if (split.Length > 1)
                        {
                            int fav = 0;
                            string[] favSplit = split[1].Split(' ');
                            if (int.TryParse(favSplit[0], out fav))
                            {
                                if (fav < 1 || fav > 3)
                                    sendToUser("Syntax: set favourite <1-3> <favorite type> <value>");
                                else
                                {
                                    if (favSplit.Length == 3)
                                    {
                                        Player.favourite f = new Player.favourite();
                                        f.type = favSplit[1];
                                        f.value = favSplit[2];

                                        myPlayer.favourites[fav-1] = f;
                                        sendToUser("You set your favourite " + f.type + " to be " + f.value, true, false, false);
                                    }
                                    else
                                    {
                                        Player.favourite f = new Player.favourite();
                                        f.type = "";
                                        f.value = "";
                                        myPlayer.favourites[fav-1] = f;
                                        sendToUser("You blank your favourite number " + fav.ToString(), true, false, false);
                                    }
                                }
                            }
                            else
                            {
                                sendToUser("Syntax: set favourite <1-3> <favorite type> <value>");
                            }
                        }
                        else
                            sendToUser("Syntax: set favourite <1-3> <favorite type> <value>");
                        break;
                    default:
                        sendToUser(syntax, true, false, false);
                        break;
                }
                myPlayer.SavePlayer();
            }
        }

        public void cmdDob(string message)
        {
            if (IsDate(message))
            {
                DateTime dob = Convert.ToDateTime(message);
                if (dob.Year >= DateTime.Now.Year)
                    sendToUser("Hmm .. not born yet, eh?", true, false, false);
                else
                {
                    sendToUser("You set your Date of Birth to " + dob.ToLongDateString(), true, false, false);
                    myPlayer.DateOfBirth = dob;
                    myPlayer.SavePlayer();
                }
            }
            else if (message == "" && myPlayer.DateOfBirth != DateTime.MinValue)
            {
                sendToUser("You blank your Date of Birth", true, false, false);
                myPlayer.DateOfBirth = DateTime.MinValue;
                myPlayer.SavePlayer();
            }
            else
            {
                sendToUser("Syntax: dob <dd/mm/yyyy>", true, false, false);
            }
        }

        public void cmdGender(string message)
        {
            switch (message.ToLower())
            {
                case "male":
                    myPlayer.Gender = 1;
                    sendToUser("You set your gender to male", true, false, false);
                    break;
                case "female":
                    myPlayer.Gender = 2;
                    sendToUser("You set your gender to female", true, false, false);
                    break;
                case "none":
                    myPlayer.Gender = 0;
                    sendToUser("You set your gender to neutral", true, false, false);
                    break;
                default:
                    sendToUser("Syntax: gender <male/female/none>", true, false, false);
                    break;
            }
            myPlayer.SavePlayer();
        }

        public void cmdPrompt(string message)
        {
            if (message == "" || AnsiColour.Colorise(message, true) == "")
            {
                sendToUser("You reset your prompt", true, false, false);
                myPlayer.Prompt = AppSettings.Default.TalkerName + ">";
                myPlayer.SavePlayer();
            }
            else
            {
                if (AnsiColour.Colorise(message, true).Length > 20)
                    sendToUser("Prompt too long, try again", true, false, false);
                else
                {
                    myPlayer.Prompt = message + "{reset}>";
                    sendToUser("You set your prompt to: " + myPlayer.Prompt, true, false, false);
                    myPlayer.SavePlayer();
                }
            }
        }

        public void cmdTimeStamp(string message)
        {
            myPlayer.TimeStampPrompt = !myPlayer.TimeStampPrompt;
            sendToUser("You " + (myPlayer.TimeStampPrompt ? "en" : "dis") + "able timestamps on prompts", true, false, false);
            myPlayer.SavePlayer();
        }

        public void cmdPrefix(string message)
        {
            if (message == "" || AnsiColour.Colorise(message, true) == "")
            {
                sendToUser("You remove your prefix", true, false, false);
                myPlayer.Prefix = "";
                myPlayer.SavePlayer();
            }
            else
            {
                if (AnsiColour.Colorise(message, true).Length > 10)
                    sendToUser("Prefix too long, try again", true, false, false);
                else
                {
                    myPlayer.Prefix = message + "{reset}";
                    sendToUser("You change your prefix to " + myPlayer.Prefix, true, false, false);
                    myPlayer.SavePlayer();
                }
            }
        }

        public void cmdTitle(string message)
        {
            if (message == "" || AnsiColour.Colorise(message, true) == "")
            {
                sendToUser("You remove your title", true, false, false);
                myPlayer.Title = "";
                myPlayer.SavePlayer();
            }
            else
            {
                if (AnsiColour.Colorise(message, true).Length > 40)
                    sendToUser("Title too long, try again", true, false, false);
                else
                {
                    myPlayer.Title = message + "{reset}";
                    sendToUser("You change your title to read: " + myPlayer.Title, true, false, false);
                    myPlayer.SavePlayer();
                }
            }
        }

        public void cmdLogonMsg(string message)
        {
            if (message == "" && myPlayer.LogonMsg == "")
                sendToUser("Syntax: logonmsg <message>", true, false, false);
            else if (message == "" && myPlayer.LogonMsg != "")
            {
                myPlayer.LogonMsg = "";
                myPlayer.SavePlayer();
                sendToUser("Logon message blanked", true, false, false);
            }
            else
            {
                myPlayer.LogonMsg = message;
                myPlayer.SavePlayer();
                sendToUser("You set your logon message to: " + myPlayer.ColourUserName + " " + message, true, false, false);
            }
        }

        public void cmdLogoffMsg(string message)
        {
            if (message == "" && myPlayer.LogoffMsg == "")
                sendToUser("Syntax: logoffmsg <message>", true, false, false);
            else if (message == "" && myPlayer.LogoffMsg != "")
            {
                myPlayer.LogoffMsg = "";
                myPlayer.SavePlayer();
                sendToUser("Logoff message blanked", true, false, false);
            }
            else
            {
                myPlayer.LogoffMsg = message;
                myPlayer.SavePlayer();
                sendToUser("You set your logoff message to: " + myPlayer.ColourUserName + " " + message, true, false, false);
            }
        }

        public void cmdEnterMsg(string message)
        {
            if (message == "" && myPlayer.EnterMsg == "")
                sendToUser("Syntax: entermsg <message>", true, false, false);
            else if (message == "" && myPlayer.EnterMsg != "")
            {
                myPlayer.EnterMsg = "";
                myPlayer.SavePlayer();
                sendToUser("Enter message blanked", true, false, false);
            }
            else
            {
                myPlayer.EnterMsg = message;
                myPlayer.SavePlayer();
                sendToUser("You set your enter message to: " + myPlayer.ColourUserName + " " + message, true, false, false);
            }
        }

        public void cmdExitMsg(string message)
        {
            if (message == "" && myPlayer.ExitMsg == "")
                sendToUser("Syntax: exitmsg <message>", true, false, false);
            else if (message == "" && myPlayer.ExitMsg != "")
            {
                myPlayer.ExitMsg = "";
                myPlayer.SavePlayer();
                sendToUser("Exit message blanked", true, false, false);
            }
            else
            {
                myPlayer.ExitMsg = message;
                myPlayer.SavePlayer();
                sendToUser("You set your exit message to: " + myPlayer.ColourUserName + " " + message, true, false, false);
            }
        }

        public void cmdDescription(string message)
        {
            if (message == "")
            {
                myPlayer.InDescriptionEditor = true;
                sendToUser("Now entering description editor. Type \".help\" for a list of editor commands", true, false, false);
                editText = myPlayer.Description;
            }
            else
            {
                if (message == "me")
                    message = myPlayer.UserName;
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("Player \"" + message + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else
                {
                    Player targ = Player.LoadPlayer(target[0], 0);
                    sendToUser(headerLine("Description: " + targ.UserName) + "\r\n" + targ.Description + "\r\n" + footerLine(), true, false, false);
                }
            }
        }

        public void cmdTagline(string message)
        {
            if (message == "" && myPlayer.Tagline == "")
            {
                sendToUser("Syntax: tagline <text>", true, false, false);
            }
            else if (message == "")
            {
                myPlayer.Tagline = "";
                myPlayer.SavePlayer();
                sendToUser("You blank your tagline", true, false, false);
            }
            else if (AnsiColour.Colorise(message, true).Length > 160)
            {
                sendToUser("Message too long - try again", true, false, false);
            }
            else
            {
                myPlayer.Tagline = message;
                myPlayer.SavePlayer();
                sendToUser("You set your tagline to: " + myPlayer.Tagline, true, false, false);
            }
        }

        public void cmdColour(string message)
        {
            if (message == "")
                sendToUser("Colour is set to: " + (myPlayer.DoColour ? "On" : "Off"), true, false, false);
            else
            {
                switch (message.ToLower())
                {
                    case "on":
                        myPlayer.DoColour = true;
                        sendToUser("Colour is set to: On", true, false, false);
                        myPlayer.SavePlayer();
                        break;
                    case "off":
                        myPlayer.DoColour = false;
                        sendToUser("Colour is set to: Off", true, false, false);
                        myPlayer.SavePlayer();
                        break;
                    default:
                        sendToUser("Syntax: Colour <ON/OFF>", true, false, false);
                        break;
                }
            }
        }

        public void cmdITag(string message)
        {
            if (message == "")
                sendToUser("Syntax: itag <player> <inform tag>", true, false, false);
            else
            {
                string[] target = (message.IndexOf(" ") > -1 ? matchPartial(message.Split(new char[] { ' ' }, 2)[0]) : matchPartial(message));
                string tag = (message.IndexOf(" ") > -1 ? message.Split(new char[] { ' ' }, 2)[1] : "");

                if (target.Length == 0)
                    sendToUser("Player \"" + (message.IndexOf(" ") > -1 ? message.Split(new char[] { ' ' }, 2)[0] : message) + "\" not found", true, false, false);
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("Player \"" + target[0] + "\" is not online at the moment", true, false, false);
                else
                {
                    if (target[0] == myPlayer.UserName)
                    {
                        sendToUser(tag == "" ? "You remove your own inform tag" : "You set your inform tag to: " + tag, true, false, false);
                        myPlayer.InformTag = tag;
                        myPlayer.SavePlayer();
                    }
                    else
                    {
                        foreach (Connection c in connections)
                        {
                            if (c.socket.Connected && c.myPlayer != null && c.myPlayer.UserName == target[0])
                            {
                                sendToUser(tag == "" ? "You remove " + c.myPlayer.UserName + "'s inform tag" : "You set " + c.myPlayer.UserName + "'s inform tag to: " + tag, true, false, false);
                                if (!c.myPlayer.InEditor)
                                    c.sendToUser("\r\n" + myPlayer.ColourUserName + (tag == "" ? " has just removed your inform tag" : " has just set your inform tag to: " + tag), true, false, false);

                                c.myPlayer.InformTag = tag;
                                c.myPlayer.SavePlayer();
                            }
                        }
                    }
                }
            }
        }

        public void cmdHChime(string message)
        {
            myPlayer.HourlyChime = !myPlayer.HourlyChime;
            sendToUser("You turn hourly chime notifications " + (myPlayer.HourlyChime ? "on" : "off"), true, false, false);
            myPlayer.SavePlayer();
        }

        public void cmdPassword(string message)
        {
            string[] split = message.Split(new char[] { ' ' });
            if (myPlayer.PlayerRank >= (int)Player.Rank.Admin && message != "" && split.Length > 1)
            {
                string[] target = matchPartial(split[0]);
                if (target.Length == 0)
                    sendToUser("No such user \"" + split[0] + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Please use the password command without a name to reset your password", true, false, false);
                else
                {
                    Player targ = Player.LoadPlayer(target[0], 0);
                    targ.Password = split[1];
                    targ.SavePlayer();
                    sendToUser("Password updated for player \"" + targ.ColourUserName + "\"", true, false, false);
                }
            }
            else if (myPlayer.PlayerRank >= (int)Player.Rank.Admin && split.Length < 2)
                sendToUser("Syntax: password <playername> <new password>", true, false, false);
            else
            {
                myState = 11;
                sendEchoOff();
                sendToUser("Please enter your current password: ", false, false, false);
            }
        }

        public void cmdPropose(string message)
        {
            if (message == "")
                sendToUser("Syntax: propose <player name>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("No such user \"" + message + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target[0]))
                    sendToUser("User \"" + target[0] + "\" is not online", true, false, false);
                else if (target[0].ToLower() == myPlayer.UserName.ToLower())
                    sendToUser("Trying to get engaged to yourself, eh?", true, false, false);
                else if (myPlayer.maritalStatus > Player.MaritalStatus.Single && myPlayer.maritalStatus < Player.MaritalStatus.Divorced)
                    sendToUser("You're already in a relationship!", true, false, false);
                else
                {
                    foreach (Connection c in connections)
                    {
                        if (c.myPlayer.UserName.ToLower() == target[0].ToLower())
                        {
                            if (myPlayer.maritalStatus == Player.MaritalStatus.ProposedTo)
                            {
                                sendToUser("You already have an outstanding proposal", true, false, false);
                            }
                            else if (c.myPlayer.maritalStatus == Player.MaritalStatus.ProposedTo)
                            {
                                sendToUser(c.myPlayer.UserName + " already has an outstanding proposal", true, false, false);
                            }
                            else if (c.myPlayer.maritalStatus > Player.MaritalStatus.Single && c.myPlayer.maritalStatus < Player.MaritalStatus.Divorced)
                            {
                                sendToUser(c.myPlayer.UserName + " is already in a relationship!", true, false, false);
                                c.sendToUser("^Y" + myPlayer.UserName + " gets down on one knee to propose, but spots your ring and changes their mind");
                            }
                            else
                            {
                                c.sendToUser(myPlayer.ColourUserName + " ^Ygets down on one knee and asks for your hand in marriage!^N");
                                sendToUser("^YYou get down on one knee and ask for " + c.myPlayer.ColourUserName + "^Y's hand in marriage!^N");
                                c.myPlayer.Spouse = myPlayer.UserName;
                                myPlayer.Spouse = c.myPlayer.UserName;
                                myPlayer.proposer = true;
                                c.myPlayer.proposer = false;
                                myPlayer.maritalStatus = Player.MaritalStatus.ProposedTo;
                                c.myPlayer.maritalStatus = Player.MaritalStatus.ProposedTo;
                                logToFile(myPlayer.UserName + " asks " + c.myPlayer.UserName + " to marry them!", "player");
                                c.myPlayer.SavePlayer();
                                myPlayer.SavePlayer();
                            }
                            return;
                        }
                    }
                }
            }
        }

        public void cmdAccept(string message)
        {
            if (myPlayer.maritalStatus != Player.MaritalStatus.ProposedTo || myPlayer.proposer)
            {
                sendToUser("You don't have any marriage offers ...", true, false, false);
            }
            else
            {
                Player p = Player.LoadPlayer(myPlayer.Spouse, 0);
                if (p != null)
                {
                    p.maritalStatus = Player.MaritalStatus.Engaged;
                    myPlayer.maritalStatus = Player.MaritalStatus.Engaged;
                    myPlayer.SavePlayer();
                    p.SavePlayer();
                    sendToAll(myPlayer.UserName + " has just become net.engaged to " + p.UserName + "! Congratulations", true);

                    foreach (Connection c in connections)
                    {
                        if (c.myPlayer != null && c.myPlayer.UserName.ToLower() == p.UserName.ToLower())
                        {
                            c.myPlayer = p;
                            return;
                        }
                    }

                    message m = new message();
                    m.To = p.UserName;
                    m.From = myPlayer.UserName;
                    m.Subject = "I said yes!";
                    m.Body = "I said yes to your marriage proposal. We are now engaged!";
                    messages.Add(m);
                    saveMessages();
                }
            }
        }

        public void cmdReject(string message)
        {
            if (myPlayer.maritalStatus != Player.MaritalStatus.ProposedTo || myPlayer.proposer)
            {
                sendToUser("You don't have any marriage offers ...", true, false, false);
            }
            else
            {
                Player p = Player.LoadPlayer(myPlayer.Spouse, 0);
                if (p != null)
                {
                    sendToUser("You reject " + myPlayer.Spouse + "'s offer of marriage", true, false, false);
                    
                    p.Spouse = "";
                    p.maritalStatus = Player.MaritalStatus.Single;
                    p.SavePlayer();

                    myPlayer.Spouse = "";
                    myPlayer.maritalStatus = Player.MaritalStatus.Single;
                    myPlayer.SavePlayer();
                    

                    foreach (Connection c in connections)
                    {
                        if (c.myPlayer != null && c.myPlayer.UserName.ToLower() == p.UserName.ToLower())
                        {
                            c.sendToUser("Sorry, " + myPlayer.UserName + " has just rejected your offer of marriage", true, false, false);
                            c.myPlayer = p;
                            return;
                        }
                    }

                    message m = new message();
                    m.To = p.UserName;
                    m.From = myPlayer.UserName;
                    m.Subject = "I said no!";
                    m.Body = "I said no to your marriage proposal. Sorry";
                    messages.Add(m);
                    saveMessages();
                }
            }
        }

        public void cmdUnpropose(string message)
        {
            if (!myPlayer.proposer || myPlayer.maritalStatus != Player.MaritalStatus.ProposedTo)
                sendToUser("You haven't made any proposals ...", true, false, false);
            else
            {
                Player p = Player.LoadPlayer(myPlayer.Spouse, 0);
                if (p != null)
                {

                    p.Spouse = "";
                    p.maritalStatus = Player.MaritalStatus.Single;
                    p.SavePlayer();
                    sendToUser("You recind your offer of marriage to " + p.UserName, true, false, false);
                    foreach (Connection c in connections)
                    {
                        if (c.myPlayer.UserName.ToLower() == p.UserName.ToLower())
                        {
                            c.sendToUser(myPlayer.UserName + " has just recinded their proposal of marriage.", true, false, false);
                            c.myPlayer = p;
                            break;
                        }
                    }
                    message m = new message();
                    m.To = p.UserName;
                    m.From = myPlayer.UserName;
                    m.Subject = "It's off";
                    m.Body = "Sorry, but I have recinded my proposal of marriage";
                    messages.Add(m);
                    saveMessages();
                }
            }
        }

        public void cmdDivorce(string message)
        {
            if (myPlayer.maritalStatus != Player.MaritalStatus.Married)
                sendToUser("You're not married!", true, false, false);
            else
            {
                Player p = Player.LoadPlayer(myPlayer.Spouse, 0);
                p.maritalStatus = Player.MaritalStatus.Divorced;
                myPlayer.maritalStatus = Player.MaritalStatus.Divorced;
                sendToUser("You divorce " + p.UserName, true, false, false);
                myPlayer.SavePlayer();
                p.SavePlayer();

                foreach (Connection c in connections)
                {
                    if (c.myPlayer != null && c.myPlayer.UserName.ToLower() == p.UserName.ToLower())
                    {
                        c.myPlayer = p;
                        c.sendToUser("You have just been divorced by " + myPlayer.UserName, true, false, false);
                        return;
                    }
                }
                message m = new message();
                m.To = p.UserName;
                m.From = myPlayer.UserName;
                m.Subject = "It's over";
                m.Body = "Sorry, but I have just divorced you";
                messages.Add(m);
                saveMessages();
            }
        }

        public void cmdMarry(string message)
        {
            if (!myPlayer.SpecialPrivs.minister)
                sendToUser("Sorry, only ministers can use this command", true, false, false);
            else if (message == "" || message.IndexOf(' ') == -1)
                sendToUser("Syntax: <name 1> <name 2>", true, false, false);
            else
            {
                string[] split = message.Split(new char[]{' '});
                string[] target1 = matchPartial(split[0]);
                string[] target2 = matchPartial(split[1]);
                if (target1.Length == 0)
                    sendToUser("Player \"" + split[0] + "\" not found", true, false, false);
                else if (target2.Length == 0)
                    sendToUser("Player \"" + split[1] + "\" not found", true, false, false);
                else if (target1.Length > 1)
                    sendToUser("Multiple matches found: " + target1.ToString() + " - Please use more letters", true, false, false);
                else if (target2.Length > 1)
                    sendToUser("Multiple matches found: " + target2.ToString() + " - Please use more letters", true, false, false);
                else if (!isOnline(target1[0]))
                    sendToUser(target1[0] + " is not online at the moment", true, false, false);
                else if (!isOnline(target2[0]))
                    sendToUser(target2[0] + " is not online at the moment", true, false, false);
                else
                {
                    // Ok, so we have the users and they are online ... next step is check to see if they are engaged to each other
                    Player t1 = Player.LoadPlayer(target1[0], 0);
                    Player t2 = Player.LoadPlayer(target2[0], 0);
                    if (t1.Spouse.ToLower() != t2.UserName.ToLower() || t2.Spouse.ToLower() != t1.UserName.ToLower())
                        sendToUser(t1.UserName + " is not engaged to " + t2.UserName, true, false, false);
                    else
                    {
                        t1.maritalStatus = Player.MaritalStatus.Married;
                        t2.maritalStatus = Player.MaritalStatus.Married;
                        sendToAll(t1.UserName + " and " + t2.UserName + " have just been joined in marriage! Congratulations", true);
                        foreach (Connection c in connections)
                        {
                            if (c.myPlayer != null)
                            {
                                if (c.myPlayer.UserName == t1.UserName)
                                {
                                    c.myPlayer = t1;
                                    c.myPlayer.SavePlayer();
                                }
                                else if (c.myPlayer.UserName == t2.UserName)
                                {
                                    c.myPlayer = t2;
                                    c.myPlayer.SavePlayer();
                                }
                            }
                        }
                    }
                }
            }
        }

        public void cmdAnnul(string message)
        {
            if (message == "")
                sendToUser("Syntax: annul <player name>", true, false, false);
            else
            {
                string[] target = matchPartial(message);
                if (target.Length == 0)
                    sendToUser("No such user \"" + message + "\"");
                else if (target.Length > 1)
                    sendToUser("Multiple matches found: " + target.ToString() + " - Please use more letters", true, false, false);
                else
                {
                    Player p = Player.LoadPlayer(target[0], 0);
                    Player t = Player.LoadPlayer(p.Spouse, 0);

                    if (p != null)
                    {
                        p.maritalStatus = Player.MaritalStatus.Single;
                        p.Spouse = "";
                        p.SavePlayer();

                        if (t != null)
                        {
                            t.maritalStatus = Player.MaritalStatus.Single;
                            t.Spouse = "";
                            t.SavePlayer();
                        }

                        if (isOnline(p.UserName) || isOnline(t.UserName))
                        {
                            foreach (Connection c in connections)
                            {
                                if (c.myPlayer != null && c.myPlayer.UserName.ToLower() == p.UserName.ToLower())
                                    c.myPlayer = p;
                                else if (c.myPlayer != null && c.myPlayer.UserName.ToLower() == t.UserName.ToLower())
                                    c.myPlayer = t;
                            }
                        }

                        sendToUser("You annul " + p.UserName + "'s marital status", true, false, false);
                    }
                    else
                        sendToUser("Sorry, something seems to have gone wrong", true, false, false);

                }
            }
        }
    }
}
