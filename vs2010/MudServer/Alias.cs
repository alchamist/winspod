using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MudServer
{
    public partial class Connection
    {

        #region Alias stuff

        public string aliasText(string preText)
        {
            return aliasText(preText, "");
        }

        public string aliasText(string preText, string stack)
        {
            // preText = the original command text, stack is the optional text supplied to the alias command
            // for each %1 in preText, take a word off the front of stack and replace
            // for each %0, dump in the remaining stack
            // Randoms are presented between curly braces, seperated by a pipe - eg {1|2|3|4}

            // First, see if there are any %1's in there
            while (preText.IndexOf("%1") > -1)
            {
                if (stack.Length == 0)
                {
                    // If there's no stack, there's nothing to replace it with!
                    preText = preText.Replace("%1", "");
                }
                else
                {
                    string[] split = stack.Split(new char[] { ' ' }, 2);
                    preText = preText.Substring(0, preText.IndexOf("%1")) + split[0] + preText.Substring(preText.IndexOf("%1") + 2);
                    if (split.Length == 1)
                        stack = "";
                    else
                        stack = split[1];
                }
            }
            // Next, see if there are any %0's
            preText = preText.Replace("%0", stack);

            // Now for the ticksy part ... sorting out if there are any randoms
            while (preText.IndexOf("{") > -1 && preText.IndexOf("}") > preText.IndexOf("{"))
            {
                string toReplace = preText.Substring(preText.IndexOf("{"), (preText.IndexOf("}") - preText.IndexOf("{")) + 1);
                if (toReplace.IndexOf("|") == -1)
                    preText = preText.Replace(toReplace, toReplace.Substring(1, toReplace.Length - 2));
                else
                {
                    string[] options = toReplace.Substring(1, toReplace.Length - 2).Split(new char[] { '|' });
                    preText = preText.Replace(toReplace, options[new Random().Next(options.Length)]);
                }

            }
            return preText;
        }

        public void cmdAlias(string message)
        {
            if (message == "")
            {
                // Listing aliases
                string output = headerLine("Aliases") + "\r\n";
                if (myPlayer.AliasList.Count == 0)
                    output += "No aliases defined\r\n";
                else
                {
                    string alist = "";
                    foreach (Player.alias a in myPlayer.AliasList)
                    {
                        alist += "{bold}{blue}" + a.aliasName + "{reset} : " + a.aliasCommand + "\r\n";
                    }
                    output += alist;
                }
                output += footerLine();
                if (myPlayer.LogonScript != "")
                {
                    output += "\r\n{bold}{blue}Logon Script {reset}: " + myPlayer.LogonScript + "\r\n" + footerLine();
                }
                sendToUser(output, true, false, false);
            }
            else
            {
                string aliasName = "";
                string aliasText = "";
                if (message.IndexOf(" ") == -1)
                    aliasName = message;
                else
                {
                    string[] split = message.Split(new char[] { ' ' }, 2);
                    aliasName = split[0];
                    aliasText = split[1];
                }

                if (myPlayer.IsAlias(aliasName))
                {
                    if (aliasText == "")
                    {
                        sendToUser("Alias \"" + aliasName + "\" deleted", true, false, false);
                        myPlayer.DeleteAlias(aliasName);
                    }
                    else
                    {
                        sendToUser("Alias \"" + aliasName + "\" updated", true, false, false);
                        myPlayer.UpdateAlias(aliasName, aliasText);
                    }
                }
                else
                {
                    if (aliasText == "")
                        sendToUser("Alias \"" + aliasName + "\" not found to delete", true, false, false);
                    else
                    {
                        sendToUser("Alias \"" + aliasName + "\" defined", true, false, false);
                        myPlayer.AddAlias(aliasName, aliasText);
                    }
                }
                myPlayer.SavePlayer();
            }
        }

        public void doLAlias()
        {
            if (myPlayer.LogonScript != "")
            {
                string[] split = myPlayer.LogonScript.Split(new char[] { ';' });
                foreach (string s in split)
                {
                    string cmd = aliasText(s);
                    ProcessLine(cmd, true);
                }
            }
        }

        public bool doAlias(string message)
        {
            bool ret = false;
            if (myPlayer.IsAlias(message))
            {
                ret = true;
                string[] split = myPlayer.GetAliasCommand(message).Split(new char[] { ';' });
                foreach (string s in split)
                {
                    ProcessLine(s, true);
                }
                doPrompt();
            }
            return ret;
        }

        #endregion

    }
}
