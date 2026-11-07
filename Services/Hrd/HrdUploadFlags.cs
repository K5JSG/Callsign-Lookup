namespace CallsignLookup.Services.Hrd
{
    // The columns HRD Logbook sets on a QSO after sending it, so that HRD's
    // own "not yet sent" lists and markers agree with what this app sent.
    // Taken from QSOs HRD itself had sent (2026-11-07).
    public static class HrdUploadFlags
    {
        public static Dictionary<string, object?> LotwSent(DateTime utcToday) => new()
        {
            ["COL_LOTW_QSL_SENT"] = "Y",
            ["COL_LOTW_QSLSDATE"] = Date(utcToday),
            ["COL_APP_HAMRADIODELUXE_LOTW_STATUS"] = "Upload OK.",
        };

        // reply: QRZ's answer ("RESULT=OK&LOGID=...&COUNT=1"), which HRD
        // keeps as the status.
        public static Dictionary<string, object?> QrzSent(DateTime utcToday, string reply) => new()
        {
            ["COL_APP_HAMRADIODELUXE_QRZCOM_SENT"] = "Y",
            ["COL_QRZCOM_QSO_UPLOAD_DATE"] = Date(utcToday),
            ["COL_QRZCOM_QSO_UPLOAD_STATUS"] = reply,
        };

        // True if HRD already has this QSO down as sent to LoTW.
        public static bool IsLotwSent(IReadOnlyDictionary<string, object?> row) =>
            row.TryGetValue("COL_LOTW_QSL_SENT", out object? value) &&
            string.Equals(Convert.ToString(value)?.Trim(), "Y", StringComparison.OrdinalIgnoreCase);

        private static string Date(DateTime day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }
}
