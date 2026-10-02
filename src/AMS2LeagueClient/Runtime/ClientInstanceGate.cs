using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AMS2LeagueClient.Runtime
{
    // Prevent two clients from the same installation from racing their update helpers.
    internal sealed class ClientInstanceGate : IDisposable
    {
        private Mutex? _mutex;

        private ClientInstanceGate(Mutex mutex) => _mutex = mutex;

        internal static ClientInstanceGate? TryAcquire(string assemblyPath)
        {
            string installation = Path.GetFullPath(assemblyPath).ToUpperInvariant();
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installation)));
            Mutex mutex;
            bool createdNew;
            try { mutex = new Mutex(true, "Local\\AMS2KRLeague.Client." + key, out createdNew); }
            catch (UnauthorizedAccessException) { return null; }
            if (createdNew) return new ClientInstanceGate(mutex);
            mutex.Dispose();
            return null;
        }

        public void Dispose()
        {
            Mutex? mutex = _mutex;
            if (mutex == null) return;
            _mutex = null;
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
