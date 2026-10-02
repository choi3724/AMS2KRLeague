using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AMS2LeagueClient.Runtime
{
    internal static class DesktopUpdateFailureLog
    {
        internal static string Write(string desktopDirectory, string installedVersion, string? targetVersion,
            string stage, Exception exception)
        {
            if (string.IsNullOrWhiteSpace(desktopDirectory) || !Directory.Exists(desktopDirectory))
                throw new DirectoryNotFoundException("The user's Desktop folder is unavailable.");

            string name = "AMS2-League-Overlay-Update-Failure-"
                + DateTime.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log";
            string path = Path.Combine(desktopDirectory, name);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.WriteLine("AMS2 League Overlay update failure");
            writer.WriteLine("TimeUtc: " + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteLine("InstalledVersion: " + installedVersion);
            writer.WriteLine("TargetVersion: " + (targetVersion ?? "unknown"));
            writer.WriteLine("Stage: " + stage);
            writer.WriteLine("InstallDirectory: " + AppContext.BaseDirectory);
            writer.WriteLine();
            writer.WriteLine(exception);
            return path;
        }
    }
}
