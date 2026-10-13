using System.Text.RegularExpressions;

namespace CallsignLookup.Services.Hrd
{
    // The QSOs highlighted in HRD Logbook's log and copied with Ctrl+C: HRD
    // puts them on the clipboard as ADIF/ADX text, one record per QSO, each
    // with its COL_PRIMARY_KEY as a PRIMARY_KEY field (found 2026-10-04) -
    // which is all the batch fill needs, as it reads the full QSOs from the
    // log itself.
    public static class HrdClipboard
    {
        // <PRIMARY_KEY>5863</PRIMARY_KEY> (ADX) or <PRIMARY_KEY:4>5863 (ADI).
        private static readonly Regex PrimaryKey =
            new(@"PRIMARY_KEY(?::\d+(?::\w)?)?\s*>\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The keys in the text, in order, each once. Empty if it isn't QSOs
        // copied from HRD.
        public static List<long> ParsePrimaryKeys(string? text)
        {
            var keys = new List<long>();
            if (string.IsNullOrEmpty(text)) return keys;
            foreach (Match match in PrimaryKey.Matches(text))
                if (long.TryParse(match.Groups[1].Value, out long key) && !keys.Contains(key))
                    keys.Add(key);
            return keys;
        }

        // From the clipboard now. Must be called on the UI thread.
        public static List<long> ReadFromClipboard()
        {
            try
            {
                return Clipboard.ContainsText() ? ParsePrimaryKeys(Clipboard.GetText()) : [];
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return [];   // another program has the clipboard open
            }
        }
    }
}
