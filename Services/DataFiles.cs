namespace CallsignLookup.Services
{
    // Read-only application data shipped in the Data\ folder next to the .exe
    // (county boundaries, CQ/ITU zone polygons, ARRL section table). Resolved
    // from AppContext.BaseDirectory rather than Application.StartupPath so the
    // services work the same from the test project, which has no WinForms
    // Application.
    internal static class DataFiles
    {
        public static string PathFor(string fileName) =>
            Path.Combine(AppContext.BaseDirectory, "Data", fileName);
    }
}
