using System.Globalization;
using System.Text;

namespace CallsignLookup.Services.Upload
{
    // Turns QSOs read from HRD's log (COL_* columns, see HrdLogDatabase) into
    // ADIF records, for sending them to LoTW (through TQSL) and to QRZ's
    // logbook. HRD's columns are named after the ADIF fields, so most map
    // straight across. The rest need converting:
    //   COL_QSO_DATE  "2026-11-07"     -> QSO_DATE 20261107
    //   COL_TIME_ON   "17:51:05.000"   -> TIME_ON 175105
    //   COL_FREQ      18071600 (Hz)    -> FREQ 18.0716 (MHz)
    //   COL_LAT       43.1234          -> LAT N043 07.404
    public static class AdifWriter
    {
        // What LoTW matches on. The station's own details (call, grid, zones,
        // park) come from the TQSL location the QSOs are signed with, so they
        // are left out here, or TQSL could reject a QSO for disagreeing.
        private static readonly string[] LotwFields =
        [
            "CALL", "QSO_DATE", "TIME_ON", "BAND", "BAND_RX", "MODE", "SUBMODE", "FREQ", "FREQ_RX", "PROP_MODE", "SAT_NAME",
        ];

        // A whole QSO for QRZ's logbook, which replaces its copy with this one.
        private static readonly string[] FullFields =
        [
            "CALL", "QSO_DATE", "TIME_ON", "BAND", "BAND_RX", "MODE", "SUBMODE", "FREQ", "FREQ_RX", "PROP_MODE", "SAT_NAME",
            "SAT_MODE", "RST_SENT", "RST_RCVD", "TX_PWR", "NAME", "QTH", "ADDRESS", "GRIDSQUARE", "LAT", "LON", "STATE", "CNTY",
            "COUNTRY", "DXCC", "CONT", "CQZ", "ITUZ", "IOTA", "IOTA_ISLAND_ID", "POTA_REF", "SOTA_REF", "WWFF_REF", "SIG",
            "SIG_INFO", "ARRL_SECT", "QSL_VIA", "COMMENT", "EMAIL", "CONTEST_ID", "SRX", "STX", "SRX_STRING", "STX_STRING",
            "OPERATOR", "STATION_CALLSIGN", "MY_GRIDSQUARE", "MY_LAT", "MY_LON", "MY_STATE", "MY_CNTY", "MY_COUNTRY", "MY_DXCC",
            "MY_CQ_ZONE", "MY_ITU_ZONE", "MY_ARRL_SECT", "MY_CITY", "MY_NAME", "MY_RIG", "MY_ANTENNA", "MY_IOTA",
            "MY_POTA_REF", "MY_SOTA_REF", "MY_WWFF_REF", "MY_SIG", "MY_SIG_INFO",
        ];

        // A file of these QSOs for TQSL.
        public static string LotwFile(IEnumerable<IReadOnlyDictionary<string, object?>> rows) =>
            "Callsign Lookup upload for LoTW\r\n<ADIF_VER:5>3.1.4 <PROGRAMID:15>Callsign Lookup <EOH>\r\n" +
            string.Concat(rows.Select(r => Record(r, LotwFields) + "\r\n"));

        // One QSO, every field the log has, for QRZ.
        public static string FullRecord(IReadOnlyDictionary<string, object?> row) => Record(row, FullFields);

        internal static string Record(IReadOnlyDictionary<string, object?> row, IEnumerable<string> fields)
        {
            var text = new StringBuilder();
            foreach (string field in fields)
            {
                string value = Value(row, field);
                if (value.Length == 0) continue;
                // ADIF counts the value's length; UTF-8 bytes are what both
                // TQSL and QRZ read.
                text.Append('<').Append(field).Append(':')
                    .Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture))
                    .Append('>').Append(value).Append(' ');
            }
            return text.Append("<EOR>").ToString();
        }

        private static string Value(IReadOnlyDictionary<string, object?> row, string field)
        {
            row.TryGetValue("COL_" + field, out object? raw);
            string text = Text(raw);
            switch (field)
            {
                case "QSO_DATE":
                {
                    string digits = Digits(text.Length >= 10 ? text[..10] : text);
                    return digits.Length == 8 ? digits : "";
                }
                case "TIME_ON":
                {
                    string digits = Digits(text.Split('.')[0]);
                    return digits.Length is 4 or 6 ? digits : "";
                }
                case "FREQ" or "FREQ_RX":
                    return raw is null || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double hz) || hz <= 0
                        ? ""
                        : (hz / 1_000_000).ToString("0.000###", CultureInfo.InvariantCulture);
                case "BAND" or "BAND_RX":
                    return text.ToLowerInvariant();
                case "LAT" or "MY_LAT":
                    return Coordinate(raw, 'N', 'S');
                case "LON" or "MY_LON":
                    return Coordinate(raw, 'E', 'W');
                case "DXCC" or "MY_DXCC" or "CQZ" or "ITUZ" or "MY_CQ_ZONE" or "MY_ITU_ZONE" or "IOTA_ISLAND_ID":
                    // HRD keeps 0 for "not set".
                    return text is "0" ? "" : text;
                default:
                    // ADI can't hold a line break inside most fields.
                    return field is "ADDRESS" or "COMMENT" ? text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ') : text;
            }
        }

        // ADIF's "XDDD MM.MMM": N043 07.404.
        private static string Coordinate(object? raw, char positive, char negative)
        {
            if (raw is null || !double.TryParse(Text(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return "";
            if (value == 0 || Math.Abs(value) > 180) return "";
            double abs = Math.Abs(value);
            int degrees = (int)abs;
            double minutes = Math.Round((abs - degrees) * 60, 3);
            if (minutes >= 60) { degrees++; minutes = 0; }
            return string.Create(CultureInfo.InvariantCulture,
                $"{(value < 0 ? negative : positive)}{degrees:000} {minutes:00.000}");
        }

        private static string Digits(string text) => new(text.Where(char.IsAsciiDigit).ToArray());

        private static string Text(object? value) => value switch
        {
            null => "",
            string s => s.Trim(),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "",
        };
    }
}
