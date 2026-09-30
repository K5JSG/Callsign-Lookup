namespace CallsignLookup
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            // Follow the Windows light/dark setting rather than always rendering
            // light - this has to be requested explicitly; it's not the default.
            Application.SetColorMode(SystemColorMode.System);

            Application.Run(new MainForm());
        }
    }
}
