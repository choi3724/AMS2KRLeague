using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using AMS2LeagueClient.Core.FutureTelemetry;

namespace AMS2LeagueClient.Core.Diagnostics
{
    internal sealed class ArchiveFailureDiagnostics
    {
        private readonly Dictionary<string, long> _counts = new Dictionary<string, long>();
        public Action<string>? Write { get; set; }

        public void Report(Exception exception, TelemetryArchiveIdentity identity, string stage,
            TelemetryStreamType? stream = null, int? chunk = null)
        {
            // Log first occurrence per attempt and powers of two,
            // not every 20 Hz retry. Do not key the aggregation by untrusted messages.
            string key = identity.AttemptId + "/" + stage + "/" + stream + "/" + exception.GetType().Name;
            long count;
            lock (_counts)
            {
                count = _counts.TryGetValue(key, out long prior) ? prior + 1 : 1;
                _counts[key] = count;
            }
            if ((count & (count - 1)) != 0) return;
            string message = exception.Message;
            // Arbitrary exception messages can contain raw JSON, paths or credentials.
            // Only the codec's numeric range/field errors are safe to copy verbatim.
            if (message.Length > 256 || !Regex.IsMatch(message, @"\AField [a-zA-Z0-9]+ (?:quantized value -?\d+ is outside \[-?\d+, -?\d+\]|is not finite|is outside its quantized range)\.\z",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) message = "UNTRUSTED_MESSAGE_OMITTED";
            string stack = string.Join(" > ", (new StackTrace(exception, false).GetFrames() ?? Array.Empty<StackFrame>())
                .Take(8).Select(frame => frame.GetMethod()).Where(method => method != null)
                .Select(method => method!.DeclaringType?.FullName + "." + method.Name));
            string schema = exception.Data["ArchiveSchemaId"] is ushort schemaId ? schemaId.ToString() : "unknown";
            string sequence = exception.Data["ArchiveSequence"] is uint value ? value.ToString() : "unknown";
            try
            {
                Write?.Invoke("attempt=" + identity.AttemptId + " stage=" + stage + " stream=" + stream
                    + " chunk=" + chunk + " schema=" + schema + " sequence=" + sequence
                    + " occurrence=" + count + " type=" + exception.GetType().Name + " hresult=" + exception.HResult.ToString("X8")
                    + " message=" + message + " stack=" + stack);
            }
            catch { /* Diagnostics must not break capture if the log disk also fails. */ }
        }
    }
}
