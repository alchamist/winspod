using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using System.Net;

namespace MudServer
{
    class AnsiColour
    {
        #region Private Varaibles

        // Create our color table
        private static List<ColorData> colorTable = new List<ColorData>();
        private static List<ColorData> codeTable = new List<ColorData>();

        #endregion

        #region Constructor

        /// <summary>
        /// Our static constructor is used to prefill our color table, so that we do not need
        /// to do so at runtime.
        /// </summary>
        static AnsiColour()
        {
            Version vrs = Assembly.GetExecutingAssembly().GetName().Version;

            // Our reset values turns everything to the default mode
            colorTable.Add(new ColorData("{reset}", "\x1B[0m", "Reset"));

            // Special for the bell
            colorTable.Add(new ColorData("{bell}", "\x1B" + (char)7, "Bell"));

            // Style Modifiers (on)
            colorTable.Add(new ColorData("{bold}", "\x1B[1m", "Bold"));
            colorTable.Add(new ColorData("{italic}", "\x1B[3m", "Italic"));
            colorTable.Add(new ColorData("{ul}", "\x1B[4m", "Underline"));
            colorTable.Add(new ColorData("{blink}", "\x1B[5m", "Blink"));
            colorTable.Add(new ColorData("{blinkf}", "\x1B[6m", "Blink Fast"));
            colorTable.Add(new ColorData("{inverse}", "\x1B[7m", "Inverse"));
            colorTable.Add(new ColorData("{strike}", "\x1B[9m", "Strikethrough"));

            // Style Modifiers (off)
            colorTable.Add(new ColorData("{!bold}", "\x1B[22m", "Bold Off"));
            colorTable.Add(new ColorData("{!italic}", "\x1B[23m", "Italic Off"));
            colorTable.Add(new ColorData("{!ul}", "\x1B[24m", "Underline Off"));
            colorTable.Add(new ColorData("{!blink}", "\x1B[25m", "Blink Off"));
            colorTable.Add(new ColorData("{!inverse}", "\x1B[27m", "Inverse Off"));
            colorTable.Add(new ColorData("{!strike}", "\x1B[29m", "Strikethrough Off"));

            // Foreground Color
            colorTable.Add(new ColorData("{black}", "\x1B[30m", "Foreground black"));
            colorTable.Add(new ColorData("{red}", "\x1B[31m", "Foreground red"));
            colorTable.Add(new ColorData("{green}", "\x1B[32m", "Foreground green"));
            colorTable.Add(new ColorData("{yellow}", "\x1B[33m", "Foreground yellow"));
            colorTable.Add(new ColorData("{blue}", "\x1B[34m", "Foreground blue"));
            colorTable.Add(new ColorData("{magenta}", "\x1B[35m", "Foreground magenta"));
            colorTable.Add(new ColorData("{cyan}", "\x1B[36m", "Foreground cyan"));
            colorTable.Add(new ColorData("{white}", "\x1B[37m", "Foreground white"));

            // Background Color
            colorTable.Add(new ColorData("{!black}", "\x1B[40m", "Background black"));
            colorTable.Add(new ColorData("{!red}", "\x1B[41m", "Background red"));
            colorTable.Add(new ColorData("{!green}", "\x1B[42m", "Background green"));
            colorTable.Add(new ColorData("{!yellow}", "\x1B[43m", "Background yellow"));
            colorTable.Add(new ColorData("{!blue}", "\x1B[44m", "Background blue"));
            colorTable.Add(new ColorData("{!magenta}", "\x1B[45m", "Background magenta"));
            colorTable.Add(new ColorData("{!cyan}", "\x1B[46m", "Background cyan"));
            colorTable.Add(new ColorData("{!white}", "\x1B[47m", "Background white"));

            // Need to add the old ewtoo commands

            colorTable.Add(new ColorData("^N", "\x1B[37;0m", "Reset"));
            colorTable.Add(new ColorData("^R", "\x1B[31;1m", "Bold Red"));
            colorTable.Add(new ColorData("^G", "\x1B[32;1m", "Bold Green"));
            colorTable.Add(new ColorData("^Y", "\x1B[33;1m", "Bold Yellow"));
            colorTable.Add(new ColorData("^B", "\x1B[34;1m", "Bold Blue"));
            colorTable.Add(new ColorData("^P", "\x1B[35;1m", "Bold Purple"));
            colorTable.Add(new ColorData("^C", "\x1B[36;1m", "Bold Cyan"));
            colorTable.Add(new ColorData("^W", "\x1B[37;1m", "Bold White"));
            colorTable.Add(new ColorData("^H", "\x1B[37;1m", "Bold White"));

            colorTable.Add(new ColorData("^r", "\x1B[31;2m", "Red"));
            colorTable.Add(new ColorData("^g", "\x1B[32;2m", "Green"));
            colorTable.Add(new ColorData("^y", "\x1B[33;2m", "Yellow"));
            colorTable.Add(new ColorData("^b", "\x1B[34;2m", "Blue"));
            colorTable.Add(new ColorData("^p", "\x1B[35;2m", "Purple"));
            colorTable.Add(new ColorData("^c", "\x1B[36;2m", "Cyan"));
            colorTable.Add(new ColorData("^w", "\x1B[37;2m", "White"));
            colorTable.Add(new ColorData("^A", "\x1B[37;2m", "Reset"));

            colorTable.Add(new ColorData("^a", "\x1B[30;1m", ""));
            colorTable.Add(new ColorData("^I", "\x1B[7m", "Italic"));
            colorTable.Add(new ColorData("^K", "\x1B[5m", "Blink"));
            colorTable.Add(new ColorData("^U", "\x1B[4m", "Underline"));
            colorTable.Add(new ColorData("^@", "\x1B" + (char)7, "Bell"));

            // Common requirements - add to codeTable! - cheating reusing the ColorData struct
            codeTable.Add(new ColorData("{tname}", AppSettings.Default.TalkerName.ToString(), "Talker Name"));
            codeTable.Add(new ColorData("{date}", DateTime.Now.ToShortDateString(), "Date now"));
            codeTable.Add(new ColorData("{time}", DateTime.Now.ToShortTimeString(), "Time now"));
            codeTable.Add(new ColorData("&t", AppSettings.Default.TalkerName.ToString(), "Talker Name"));
            codeTable.Add(new ColorData("&d", DateTime.Now.ToShortDateString(), "Date now"));
            codeTable.Add(new ColorData("&n", DateTime.Now.ToShortTimeString(), "Time now"));
            codeTable.Add(new ColorData("&v", vrs.ToString(), "Version"));

        } // End of AnsiColor

        #endregion

        #region Methods


        public static string Colorise(string stringToColour)
        {
            return Colorise(stringToColour, false);
        }

        public static string Colorise(string stringToColour, bool removeColour)
        {
            stringToColour += "{reset}";
            // Loop through our table
            foreach (ColorData colorData in colorTable)
            {
                // Replace our identifier with our code
                if (!removeColour)
                    stringToColour = stringToColour.Replace(colorData.Identifier, colorData.Code);
                else
                    stringToColour = stringToColour.Replace(colorData.Identifier, "");
            }

            // Loop through the code table - not conditional on removeColour
            foreach (ColorData code in codeTable)
            {
                stringToColour = stringToColour.Replace(code.Identifier, code.Code);
            }

            // Width justification
            int sLen = stringToColour.Length;
            string output = "";

            if (sLen > 80)
            {
                int len = 0;
                for (int i = 0; i < sLen; i++)
                {

                    string test = stringToColour.Substring(i, 1);
                    if (test == "\x1B[" || test == ((char)27).ToString())
                    {
                        len -= (stringToColour.IndexOf("m", i) - i);
                    }
                    else if (test == Environment.NewLine || test == "\n")
                        len = 0;
                    else
                        len++;

                    if (len > 73 && test == " " && stringToColour.IndexOf(" ", i) > 80)
                    {
                        //Debug.Print(len.ToString() + ":" + stringToColour.IndexOf(" ", i).ToString());
                        Debug.Print(len.ToString());
                        output += "\r\n";
                        len = 0;
                    }
                    else
                        output += test;
                }
            }
            else
                output = stringToColour;

            // Return our colored string
            //return (stringToColour);
            return output;
        } // End of Colorize Function

        #endregion
    }

    struct ColorData
    {
        #region Private Variables

        string identifier;
        string code;
        string definition;

        #endregion

        #region Public Properties

        public string Code
        {
            get { return code; }
        } // End of ReadOnly Code

        public string Identifier
        {
            get { return identifier; }
        } // End of ReadOnly Identifier

        public string Definition
        {
            get { return definition; }
        } // End of ReadOnly Definition

        #endregion

        public ColorData(string identifier, string code, string definition)
        {
            // Set our values
            this.identifier = identifier;
            this.code = code;
            this.definition = definition;
        } // End of ColorData Constructor

    }
}
