using System;
using System.IO;
using System.Text;

namespace AMS2LeagueClient.Core.Diagnostics
{
    public sealed class FileLogger
    {
        private readonly object _gate = new object();

        public FileLogger(string directory)
        {
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, "client-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
        }

        public string FilePath { get; }

        public void Info(string eventName, string details)
        {
            Write("INFO", eventName, details);
        }

        public void Warning(string eventName, string details)
        {
            Write("WARN", eventName, details);
        }

        public void Error(string eventName, Exception exception)
        {
            Write("ERROR", eventName, exception.GetType().Name + ": " + exception.Message);
        }

        private void Write(string level, string eventName, string details)
        {
            string sanitized = (details ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
            string line = DateTimeOffset.Now.ToString("O") + " [" + level + "] " + eventName + " " + sanitized + Environment.NewLine;
            lock (_gate)
            {
                File.AppendAllText(FilePath, line, new UTF8Encoding(false));
            }
        }
    }
}
