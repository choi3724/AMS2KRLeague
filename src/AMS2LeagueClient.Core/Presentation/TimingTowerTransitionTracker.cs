using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class TimingTowerTransition
    {
        public TimingTowerTransition(int participantIndex, int fromIndex, int toIndex, bool statusChanged)
        {
            ParticipantIndex = participantIndex;
            FromIndex = fromIndex;
            ToIndex = toIndex;
            StatusChanged = statusChanged;
        }

        public int ParticipantIndex { get; }
        public int FromIndex { get; }
        public int ToIndex { get; }
        public bool StatusChanged { get; }
        public bool IsReorder => FromIndex >= 0 && FromIndex != ToIndex;
        public int RowDelta => FromIndex < 0 ? 0 : FromIndex - ToIndex;
    }

    public sealed class TimingTowerTransitionTracker
    {
        private readonly Dictionary<int, int> _indexes = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _statuses = new Dictionary<int, string>();

        public IReadOnlyList<TimingTowerTransition> Observe(IReadOnlyList<RankingRowViewModel> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var transitions = new List<TimingTowerTransition>();
            for (int index = 0; index < rows.Count; index++)
            {
                RankingRowViewModel row = rows[index];
                int oldIndex = _indexes.TryGetValue(row.ParticipantIndex, out int previousIndex) ? previousIndex : -1;
                bool statusChanged = _statuses.TryGetValue(row.ParticipantIndex, out string? previousStatus)
                    && !string.Equals(previousStatus, row.Status, StringComparison.Ordinal);
                transitions.Add(new TimingTowerTransition(row.ParticipantIndex, oldIndex, index, statusChanged));
            }

            _indexes.Clear();
            _statuses.Clear();
            for (int index = 0; index < rows.Count; index++)
            {
                _indexes[rows[index].ParticipantIndex] = index;
                _statuses[rows[index].ParticipantIndex] = rows[index].Status;
            }
            return transitions;
        }

        public void Reset()
        {
            _indexes.Clear();
            _statuses.Clear();
        }
    }
}
