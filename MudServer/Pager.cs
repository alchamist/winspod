using System;
using System.Collections.Generic;

namespace MudServer
{
    public partial class Connection
    {

        #region Pager

        // Called from sendToUser with text that's already been through AnsiColour.Colorise
        // (so already width-wrapped to this connection's TermWidth) - this only adds a
        // second dimension of chunking, by line count, using the TermHeight NAWS also
        // reports but nothing used until now. Returns false (does nothing) if the text
        // fits on one screen, or paging is off - the caller falls back to writing it
        // straight through exactly as it always has.
        private bool beginPager(string colorisedText)
        {
            string[] lines = colorisedText.Replace("\r\n", "\n").Split('\n');
            int pageSize = Math.Max(TermHeight - 1, 5); // leave a line for the prompt; guard a tiny/misreported height

            if (lines.Length <= pageSize)
                return false;

            pagerLines = new List<string>(lines);
            inPager = true;
            sendPageChunk();
            return true;
        }

        private void sendPageChunk()
        {
            int pageSize = Math.Max(TermHeight - 1, 5);
            int take = Math.Min(pageSize, pagerLines.Count);

            for (int i = 0; i < take; i++)
                Writer.WriteLine(pagerLines[i]);
            pagerLines.RemoveRange(0, take);

            if (pagerLines.Count > 0)
                Writer.Write(AnsiColour.Colorise("{bold}{yellow}-- More -- (" + pagerLines.Count + " line" + (pagerLines.Count == 1 ? "" : "s") + " left - enter to continue, q to stop){reset} ", !myPlayer.DoColour, TermWidth));
            else
                inPager = false;

            Writer.Flush();
        }

        private void continuePager(string input)
        {
            if (input.Trim().ToLower() == "q")
            {
                pagerLines.Clear();
                inPager = false;
                sendToUser("\r\n{bold}{yellow}-- Paging stopped --{reset}", true, false, false);
                return;
            }

            sendPageChunk();
            if (!inPager)
                doPrompt();
        }

        #endregion

    }
}
