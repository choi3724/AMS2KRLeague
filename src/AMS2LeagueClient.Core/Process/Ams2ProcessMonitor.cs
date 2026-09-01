using System;

namespace AMS2LeagueClient.Core.Process
{
    public sealed class Ams2ProcessInfo
    {
        public Ams2ProcessInfo(int processId, string processName)
        {
            ProcessId = processId;
            ProcessName = processName;
        }

        public int ProcessId { get; }
        public string ProcessName { get; }
    }

    public sealed class Ams2ProcessMonitor
    {
        private static readonly string[] ProcessNames = { "AMS2AVX", "AMS2" };

        public Ams2ProcessInfo? FindRunningProcess()
        {
            foreach (string name in ProcessNames)
            {
                System.Diagnostics.Process[] processes;
                try
                {
                    processes = System.Diagnostics.Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }

                try
                {
                    foreach (System.Diagnostics.Process process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                return new Ams2ProcessInfo(process.Id, process.ProcessName);
                            }
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }
                }
                finally
                {
                    foreach (System.Diagnostics.Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            return null;
        }
    }
}
