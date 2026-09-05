using System;
using System.Collections.Generic;
using System.Globalization;

namespace AMS2LeagueClient.Core.Presentation
{
    /// <summary>
    /// One participant's change between two Timing Tower frames. Drives the
    /// broadcast transitions: row reorder slide, position gain/loss flash and
    /// number roll, status badge pop and the session-fastest-lap sweep.
    /// </summary>
    public sealed class TimingTowerTransition
    {
        public const string FastestLapStatus = "BEST";

        public TimingTowerTransition(int participantIndex, int fromIndex, int toIndex, bool statusChanged)
            : this(participantIndex, fromIndex, toIndex, statusChanged, 0, 0, string.Empty, string.Empty)
        {
        }

        public TimingTowerTransition(
            int participantIndex,
            int fromIndex,
            int toIndex,
            bool statusChanged,
            int previousPosition,
            int position,
            string previousStatus,
            string status)
        {
            ParticipantIndex = participantIndex;
            FromIndex = fromIndex;
            ToIndex = toIndex;
            StatusChanged = statusChanged;
            PreviousPosition = previousPosition;
            Position = position;
            PreviousStatus = previousStatus ?? string.Empty;
            Status = status ?? string.Empty;
        }

        public int ParticipantIndex { get; }
        public int FromIndex { get; }
        public int ToIndex { get; }
        public bool StatusChanged { get; }

        /// <summary>Numeric league position in the previous frame; 0 when unknown or new.</summary>
        public int PreviousPosition { get; }

        /// <summary>Numeric league position in this frame; 0 when not a number.</summary>
        public int Position { get; }
        public string PreviousStatus { get; }
        public string Status { get; }

        /// <summary>The participant was not in the visible tower window before this frame.</summary>
        public bool IsNew => FromIndex < 0;
        public bool IsReorder => FromIndex >= 0 && FromIndex != ToIndex;
        public int RowDelta => FromIndex < 0 ? 0 : FromIndex - ToIndex;
        public bool PositionGained => PreviousPosition > 0 && Position > 0 && Position < PreviousPosition;
        public bool PositionLost => PreviousPosition > 0 && Position > 0 && Position > PreviousPosition;
        public bool BecameFastestLap
            => StatusChanged && string.Equals(Status, FastestLapStatus, StringComparison.Ordinal);
    }

    public sealed class TimingTowerTransitionTracker
    {
        private readonly Dictionary<int, int> _indexes = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _statuses = new Dictionary<int, string>();
        private readonly Dictionary<int, int> _positions = new Dictionary<int, int>();

        public IReadOnlyList<TimingTowerTransition> Observe(IReadOnlyList<RankingRowViewModel> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var transitions = new List<TimingTowerTransition>();
            for (int index = 0; index < rows.Count; index++)
            {
                RankingRowViewModel row = rows[index];
                int oldIndex = _indexes.TryGetValue(row.ParticipantIndex, out int previousIndex) ? previousIndex : -1;
                string previousStatus = _statuses.TryGetValue(row.ParticipantIndex, out string? knownStatus) ? knownStatus : string.Empty;
                bool statusChanged = oldIndex >= 0 && !string.Equals(previousStatus, row.Status ?? string.Empty, StringComparison.Ordinal);
                int previousPosition = oldIndex >= 0 && _positions.TryGetValue(row.ParticipantIndex, out int knownPosition) ? knownPosition : 0;
                transitions.Add(new TimingTowerTransition(
                    row.ParticipantIndex,
                    oldIndex,
                    index,
                    statusChanged,
                    previousPosition,
                    ParsePosition(row.Position),
                    previousStatus,
                    row.Status ?? string.Empty));
            }

            _indexes.Clear();
            _statuses.Clear();
            _positions.Clear();
            for (int index = 0; index < rows.Count; index++)
            {
                _indexes[rows[index].ParticipantIndex] = index;
                _statuses[rows[index].ParticipantIndex] = rows[index].Status ?? string.Empty;
                _positions[rows[index].ParticipantIndex] = ParsePosition(rows[index].Position);
            }
            return transitions;
        }

        public void Reset()
        {
            _indexes.Clear();
            _statuses.Clear();
            _positions.Clear();
        }

        /// <summary>Parses the numeric part of a tower position label such as "P12"; 0 when absent.</summary>
        public static int ParsePosition(string? label)
        {
            if (string.IsNullOrEmpty(label)) return 0;
            int start = 0;
            while (start < label.Length && !char.IsDigit(label[start])) start++;
            int end = start;
            while (end < label.Length && char.IsDigit(label[end])) end++;
            return end > start
                && int.TryParse(label.Substring(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 0;
        }
    }
}
