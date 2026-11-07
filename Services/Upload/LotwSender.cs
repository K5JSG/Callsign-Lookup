using System.Diagnostics;
using System.Text;

namespace CallsignLookup.Services.Upload
{
    // Sent: LoTW has the QSOs (newly, or already had them). NeedsPassword:
    // TQSL wants the callsign certificate's password to sign them.
    public sealed record LotwResult(bool Sent, bool NeedsPassword, int ExitCode, string Message);

    // Signs QSOs and sends them to LoTW with TQSL, the program HRD uses for
    // it, run with no window:
    //   tqsl -q -x -d -u -a compliant -l "<location>" <file.adi>
    // -q quiet, -x exit when done, -d no date-range question, -u upload,
    // -a compliant: sign the QSOs LoTW will take and skip duplicates.
    public static class LotwSender
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

        public static async Task<LotwResult> SendAsync(
            string tqslPath, string location, IEnumerable<IReadOnlyDictionary<string, object?>> rows,
            string? password = null, CancellationToken token = default)
        {
            string file = Path.Combine(Path.GetTempPath(), $"Callsign Lookup LoTW {Guid.NewGuid():N}.adi");
            try
            {
                await File.WriteAllTextAsync(file, AdifWriter.LotwFile(rows), new UTF8Encoding(false), token);

                var start = new ProcessStartInfo(tqslPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                foreach (string arg in new[] { "-q", "-x", "-d", "-u", "-a", "compliant", "-l", location })
                    start.ArgumentList.Add(arg);
                if (!string.IsNullOrEmpty(password))
                {
                    start.ArgumentList.Add("-p");
                    start.ArgumentList.Add(password);
                }
                start.ArgumentList.Add(file);

                using var process = Process.Start(start) ?? throw new InvalidOperationException("TQSL didn't start.");
                var output = process.StandardOutput.ReadToEndAsync(token);
                var errors = process.StandardError.ReadToEndAsync(token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(Timeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch (InvalidOperationException) { }
                    return new LotwResult(false, false, -1,
                        token.IsCancellationRequested ? "Cancelled." : $"TQSL didn't finish within {Timeout.TotalMinutes:0} minutes and was stopped.");
                }

                string said = Clean((await output) + "\n" + (await errors), password);
                return Interpret(process.ExitCode, said);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return new LotwResult(false, false, -1, $"Couldn't run TQSL: {ex.Message}");
            }
            finally
            {
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        // TQSL's exit codes (TQSL's command-line help).
        internal static LotwResult Interpret(int exitCode, string said)
        {
            string detail = said.Trim().Length > 0 ? " TQSL said: " + said.Trim() : "";
            bool password = said.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                            said.Contains("passphrase", StringComparison.OrdinalIgnoreCase);
            return exitCode switch
            {
                0 => new LotwResult(true, false, 0, "Sent to LoTW."),
                8 => new LotwResult(true, false, 8, "LoTW already had it (duplicate) - nothing new to send."),
                9 => new LotwResult(true, false, 9, "Sent to LoTW. Some were duplicates LoTW already had."),
                1 => new LotwResult(false, password, 1, "TQSL was cancelled." + detail),
                2 => new LotwResult(false, false, 2, "LoTW rejected the upload." + detail),
                3 => new LotwResult(false, false, 3, "LoTW gave an unexpected answer." + detail),
                6 => new LotwResult(false, false, 6, "TQSL couldn't open the file it was given." + detail),
                10 => new LotwResult(false, false, 10, "TQSL didn't understand the command (is the location name right?)." + detail),
                11 => new LotwResult(false, false, 11, "TQSL couldn't reach LoTW (no internet, or LoTW is down)." + detail),
                _ when password => new LotwResult(false, true, exitCode, "TQSL needs the callsign certificate's password."),
                _ => new LotwResult(false, false, exitCode, $"TQSL failed (exit code {exitCode})." + detail),
            };
        }

        // TQSL's words, without the password (should it ever echo it) and
        // kept short.
        private static string Clean(string text, string? password)
        {
            if (!string.IsNullOrEmpty(password)) text = text.Replace(password, "***");
            text = string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
            return text.Length > 600 ? text[..600] + "..." : text;
        }
    }
}
