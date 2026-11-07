using Microsoft.Win32;

namespace CallsignLookup.Services.Hrd
{
    // What HRD Logbook itself uses to send QSOs to LoTW and QRZ, read from its
    // settings so this app sends them the same way:
    //   HKCU\Software\Amateur Radio\HRD User Profile\<call>   QRZApiKey, LOTWStation
    //   HKCU\Software\Amateur Radio\HRD Logbook\Options       CPrefsLOTW::m_strExecutable
    // LOTWStation is the TQSL location HRD last used for that callsign (a
    // loaded location file sets it). Only these three values are read: the
    // same keys hold upload passwords, which this app never touches. The QRZ
    // key is never shown, logged or saved by this app.
    public static class HrdUploadSettings
    {
        private const string ProfilePath = @"Software\Amateur Radio\HRD User Profile";
        private const string OptionsPath = @"Software\Amateur Radio\HRD Logbook\Options";
        public const string DefaultTqslPath = @"C:\Program Files (x86)\TrustedQSL\tqsl.exe";

        // TQSL as HRD runs it, or the usual install folder. Null if neither exists.
        public static string? TqslPath()
        {
            using var key = Registry.CurrentUser.OpenSubKey(OptionsPath);
            string configured = (key?.GetValue("CPrefsLOTW::m_strExecutable") as string)?.Trim() ?? "";
            if (configured.Length > 0 && File.Exists(configured)) return configured;
            return File.Exists(DefaultTqslPath) ? DefaultTqslPath : null;
        }

        // HRD's QRZ logbook key for the QSO's station callsign: that call's
        // own profile, then its base call (K5JSG for K5JSG/P), then the only
        // profile with a key. Empty if there's none.
        public static string QrzApiKey(string stationCallsign) => ProfileValue(stationCallsign, "QRZApiKey");

        // The TQSL location HRD last used for this station callsign, "" if none.
        public static string LotwStation(string stationCallsign) => ProfileValue(stationCallsign, "LOTWStation", fallBackToAny: false);

        private static string ProfileValue(string stationCallsign, string valueName, bool fallBackToAny = true)
        {
            using var root = Registry.CurrentUser.OpenSubKey(ProfilePath);
            if (root == null) return "";
            var names = root.GetSubKeyNames().Where(n => !n.Equals("Options", StringComparison.OrdinalIgnoreCase)).ToList();

            string call = stationCallsign.Trim();
            string baseCall = BaseCall(call);
            var order = names.Where(n => n.Equals(call, StringComparison.OrdinalIgnoreCase))
                .Concat(names.Where(n => !n.Equals(call, StringComparison.OrdinalIgnoreCase) &&
                                         n.Equals(baseCall, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            foreach (string name in order)
                if (Read(root, name, valueName) is { Length: > 0 } value) return value;

            if (!fallBackToAny) return "";
            var withValue = names.Select(n => Read(root, n, valueName)).Where(v => v.Length > 0).Distinct().ToList();
            return withValue.Count == 1 ? withValue[0] : "";
        }

        private static string Read(RegistryKey root, string name, string valueName)
        {
            using var key = root.OpenSubKey(name);
            return (key?.GetValue(valueName) as string)?.Trim() ?? "";
        }

        // K5JSG/P -> K5JSG, VE3/K5JSG -> K5JSG: the longest part.
        internal static string BaseCall(string call) =>
            call.Split('/').OrderByDescending(p => p.Length).FirstOrDefault() ?? call;
    }
}
