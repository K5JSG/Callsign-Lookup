using System.Text.Json;

namespace CallsignLookup.Services
{
    // The user's QRZ login, kept in %LocalAppData%\Callsign Lookup\settings.json.
    // The password is stored DPAPI-encrypted (see DpapiProtector), so the file
    // is useless to anyone but this Windows user on this PC.
    public sealed class AppSettings
    {
        public string QrzUsername { get; set; } = "";
        public string QrzPasswordProtected { get; set; } = "";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string Folder
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Callsign Lookup");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static string FilePath => Path.Combine(Folder, "settings.json");

        public bool HasQrzLogin => QrzUsername.Length > 0 && QrzPasswordProtected.Length > 0;

        public string GetQrzPassword()
        {
            try
            {
                return DpapiProtector.IsProtected(QrzPasswordProtected)
                    ? DpapiProtector.Unprotect(QrzPasswordProtected)
                    : "";
            }
            catch (InvalidOperationException)
            {
                // Settings file copied from another user/PC - can't decrypt;
                // treat as no password so the user is asked again.
                return "";
            }
        }

        public void SetQrzPassword(string password) =>
            QrzPasswordProtected = password.Length == 0 ? "" : DpapiProtector.Protect(password);

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Unreadable/corrupt settings - start fresh; the user just logs in again.
            }
            return new AppSettings();
        }

        public void Save() =>
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
