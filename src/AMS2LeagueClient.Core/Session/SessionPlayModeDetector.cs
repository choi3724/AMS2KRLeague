using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Core.Session
{
    /// <summary>Read-only evidence from AMS2's own online log, never participant-count heuristics.</summary>
    public sealed class SessionPlayModeDetector
    {
        private readonly object _gate = new object();
        private readonly string _logDirectory;
        private readonly string _historyPath;
        private readonly Dictionary<string, (long Length, DateTime Mtime, OnlineSessionTimeline Timeline)> _cache = new Dictionary<string, (long Length, DateTime Mtime, OnlineSessionTimeline Timeline)>();
        private readonly Dictionary<DateTimeOffset, OnlineSessionTimeline> _history = new Dictionary<DateTimeOffset, OnlineSessionTimeline>();
        private volatile SessionPlayMode _currentMode;
        private long _refreshedAtTicks;
        private string _diagnostic = "로그 확인 대기";
        private const int MaximumLogBytes = 16 * 1024 * 1024;

        public SessionPlayModeDetector(string logDirectory, string historyPath)
        {
            _logDirectory = Path.GetFullPath(logDirectory);
            _historyPath = Path.GetFullPath(historyPath);
            try
            {
                if (File.Exists(_historyPath) && new FileInfo(_historyPath).Length <= MaximumLogBytes)
                    foreach (var entry in JsonSerializer.Deserialize<List<OnlineSessionTimeline>>(File.ReadAllBytes(_historyPath)) ?? new List<OnlineSessionTimeline>())
                        if (entry.IsValid()) _history[entry.StartedAtUtc] = entry;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            { _diagnostic = "이전 판정 이력을 읽지 못함"; }
        }

        public SessionPlayMode CurrentMode
        {
            get { return DateTime.UtcNow.Ticks - System.Threading.Interlocked.Read(ref _refreshedAtTicks) <= TimeSpan.FromSeconds(15).Ticks ? _currentMode : SessionPlayMode.Unknown; }
        }
        public string Diagnostic { get { lock (_gate) return _diagnostic; } }

        public void Refresh()
        {
            DateTimeOffset? started = null;
            try
            {
                var running = System.Diagnostics.Process.GetProcessesByName("AMS2AVX")
                    .Concat(System.Diagnostics.Process.GetProcessesByName("AMS2")).ToArray();
                try { if (running.Length == 1) started = running[0].StartTime.ToUniversalTime(); }
                finally { foreach (var process in running) process.Dispose(); }
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            { /* An unavailable process identity cannot authorize the current log. */ }
            Refresh(DateTimeOffset.UtcNow, started, TimeZoneInfo.Local);
        }

        public void Refresh(DateTimeOffset now, DateTimeOffset? processStartedAt, TimeZoneInfo timeZone)
        {
            lock (_gate)
            {
                SessionPlayMode nextMode = SessionPlayMode.Unknown;
                _diagnostic = "현재 실행의 로그를 확인할 수 없음";
                bool changed = false;
                foreach (string name in new[] { "online.5.log", "online.4.log", "online.3.log", "online.2.log", "online.1.log", "online.log" })
                {
                    string path = Path.Combine(_logDirectory, name);
                    try
                    {
                        var info = new FileInfo(path);
                        if (!info.Exists || info.Length < 1 || info.Length > MaximumLogBytes) continue;
                        bool parsedChanged = false;
                        if (!_cache.TryGetValue(path, out var cached) || cached.Length != info.Length || cached.Mtime != info.LastWriteTimeUtc)
                        {
                            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            byte[] bytes = new byte[checked((int)info.Length)];
                            int read = 0;
                            while (read < bytes.Length)
                            {
                                int count = file.Read(bytes, read, bytes.Length - read);
                                if (count == 0) break;
                                read += count;
                            }
                            if (read != bytes.Length) continue;
                            string text = System.Text.Encoding.UTF8.GetString(bytes);
                            if (text.Length > MaximumLogBytes || !text.EndsWith('\n')) continue;
                            var parsed = Parse(text, timeZone);
                            if (parsed == null || parsed.ObservedThroughUtc > now.AddSeconds(2)) continue;
                            cached = (info.Length, info.LastWriteTimeUtc, parsed);
                            _cache[path] = cached;
                            parsedChanged = true;
                        }
                        var timeline = cached.Timeline.Copy();
                        bool current = name == "online.log" && processStartedAt.HasValue
                            && timeline.StartedAtUtc >= processStartedAt.Value.AddSeconds(-2)
                            && timeline.StartedAtUtc <= processStartedAt.Value.AddMinutes(2);
                        if (current)
                        {
                            nextMode = timeline.Transitions.LastOrDefault()?.Mode ?? SessionPlayMode.SinglePlayer;
                            // Do not authorize new multiplayer samples beyond the last actual log event.
                            // A stalled writer must not leave a permanent multiplayer permission behind.
                            if (nextMode == SessionPlayMode.Multiplayer && now - timeline.ObservedThroughUtc > TimeSpan.FromSeconds(15))
                                nextMode = SessionPlayMode.Unknown;
                            else if (nextMode != SessionPlayMode.Multiplayer)
                                timeline.ObservedThroughUtc = now;
                            _diagnostic = nextMode == SessionPlayMode.Unknown ? "접속 확인 로그 대기" : "게임 접속 로그 자동 판정";
                        }
                        changed |= parsedChanged || !_history.TryGetValue(timeline.StartedAtUtc, out var prior)
                            || prior.ObservedThroughUtc != timeline.ObservedThroughUtc;
                        _history[timeline.StartedAtUtc] = timeline;
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
                    { if (name == "online.log") _diagnostic = "게임 접속 로그 읽기 실패"; }
                }
                _currentMode = nextMode;
                System.Threading.Interlocked.Exchange(ref _refreshedAtTicks, now.UtcDateTime.Ticks);
                if (changed)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
                        string temporary = _historyPath + ".tmp";
                        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(_history.Values.OrderBy(x => x.StartedAtUtc)));
                        File.Move(temporary, _historyPath, true);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    { _diagnostic = "자동 판정 이력 저장 실패 · 재시작 후 재확인 필요"; }
                }
            }
        }

        public SessionPlayMode Classify(DateTimeOffset start, DateTimeOffset end)
        {
            if (start == default || end < start || end > DateTimeOffset.UtcNow.AddSeconds(2)) return SessionPlayMode.Unknown;
            lock (_gate)
            {
                var timeline = _history.Values.Where(x => x.StartedAtUtc <= start).OrderByDescending(x => x.StartedAtUtc).FirstOrDefault();
                if (timeline == null || end > timeline.ObservedThroughUtc) return SessionPlayMode.Unknown;
                if (_history.Keys.Any(x => x > start && x <= end)) return SessionPlayMode.Unknown;
                SessionPlayMode mode = SessionPlayMode.SinglePlayer;
                foreach (var transition in timeline.Transitions)
                {
                    if (transition.AtUtc <= start) mode = transition.Mode;
                    else if (transition.AtUtc <= end && transition.Mode != mode) return SessionPlayMode.Unknown;
                }
                return mode;
            }
        }

        public bool CanUpload(DateTimeOffset? start, DateTimeOffset? end)
            => start.HasValue && end.HasValue && end.Value <= DateTimeOffset.UtcNow.AddSeconds(-10)
                && Classify(start.Value, end.Value) == SessionPlayMode.Multiplayer;

        public static string WireValue(SessionPlayMode mode) => mode switch
        {
            SessionPlayMode.SinglePlayer => "SINGLE_PLAYER",
            SessionPlayMode.Multiplayer => "MULTIPLAYER",
            _ => "UNKNOWN"
        };

        public static OnlineSessionTimeline? Parse(string text, TimeZoneInfo zone)
        {
            string Header(string key) => Regex.Match(text, @"(?m)^\[Session\]\[" + key + @"\][ \t]+([^\r\n]+)").Groups[1].Value.Trim();
            if (Header("Platform") != "PC" || !Regex.IsMatch(Header("Exe"), @"(?i)(?:^|[\\/])AMS2(?:AVX)?\.exe$")) return null;
            if (!DateTime.TryParseExact(Header("Date") + " " + Header("Time"), "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) return null;
            DateTimeOffset boot = TimeZoneInfo.ConvertTimeToUtc(local, zone);
            if (boot.ToString("HH:mm:ss", CultureInfo.InvariantCulture) != Header("UTC")) return null;
            var result = new OnlineSessionTimeline { StartedAtUtc = boot, ObservedThroughUtc = boot };
            DateTimeOffset previous = boot;
            foreach (string line in text.Split('\n'))
            {
                var stamp = Regex.Match(line, @"^\[(\d{2}:\d{2}:\d{2}:\d{3})\]");
                if (!stamp.Success) continue;
                if (!TimeSpan.TryParseExact(stamp.Groups[1].Value, @"hh\:mm\:ss\:fff", CultureInfo.InvariantCulture, out var time)) return null;
                var at = new DateTimeOffset(previous.UtcDateTime.Date, TimeSpan.Zero) + time;
                if (at < previous.AddHours(-12)) at = at.AddDays(1);
                if (at > previous.AddHours(12)) at = at.AddDays(-1);
                if (at < boot.AddSeconds(-1) || at < previous.AddSeconds(-2)) return null;
                if (at < previous) at = previous;
                previous = at;
                result.ObservedThroughUtc = at;
                SessionPlayMode? mode = null;
                string message = Regex.Match(line, @"^\[\d{2}:\d{2}:\d{2}:\d{3}\]\[0x[0-9a-fA-F]+\]\s+\[Info\s*\]\s+(.+)$").Groups[1].Value;
                if (message.StartsWith("[GameManager::JoinGame_Steam] [GameManager] Joining Steam lobby", StringComparison.Ordinal)) mode = SessionPlayMode.Unknown;
                if (message.StartsWith("[GameManager::LobbyCreatedCallback] [GameManager] Created p2p game lobby", StringComparison.Ordinal)
                    || message.StartsWith("[GameManager::HandleLobbyChatMemberDetails] [GameManager] Finished joining game lobby", StringComparison.Ordinal)
                    || message.StartsWith("[SessionMessageManager::MemberConnected] [SessionNetworking] Session networking joined session", StringComparison.Ordinal)) mode = SessionPlayMode.Multiplayer;
                if (message.StartsWith("[GameManager::LeaveGame_Steam] [GameManager] Leaving session", StringComparison.Ordinal)
                    || message.StartsWith("[SessionMessageManager::MemberDisconnected] [SessionNetworking] Session networking leaving session", StringComparison.Ordinal)) mode = SessionPlayMode.SinglePlayer;
                if (mode.HasValue && (result.Transitions.LastOrDefault()?.Mode ?? SessionPlayMode.SinglePlayer) != mode.Value)
                    result.Transitions.Add(new OnlineSessionTransition { AtUtc = at, Mode = mode.Value });
            }
            return result.IsValid() ? result : null;
        }
    }

    public sealed class OnlineSessionTransition
    {
        public DateTimeOffset AtUtc { get; set; }
        public SessionPlayMode Mode { get; set; }
    }
    public sealed class OnlineSessionTimeline
    {
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset ObservedThroughUtc { get; set; }
        public List<OnlineSessionTransition> Transitions { get; set; } = new List<OnlineSessionTransition>();
        public bool IsValid()
        {
            if (StartedAtUtc == default || ObservedThroughUtc < StartedAtUtc || Transitions == null) return false;
            var previous = StartedAtUtc;
            foreach (var entry in Transitions)
            {
                if (entry == null || entry.AtUtc < previous || entry.AtUtc > ObservedThroughUtc || !Enum.IsDefined(entry.Mode)) return false;
                previous = entry.AtUtc;
            }
            return true;
        }
        public OnlineSessionTimeline Copy() => new OnlineSessionTimeline() { StartedAtUtc = StartedAtUtc, ObservedThroughUtc = ObservedThroughUtc, Transitions = Transitions.Select(x => new OnlineSessionTransition { AtUtc = x.AtUtc, Mode = x.Mode }).ToList() };
    }
}