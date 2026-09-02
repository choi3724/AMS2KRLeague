using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AMS2LeagueClient.Core.CompactTelemetry
{
    public sealed class CompactParticipantDictionaryEntry
    {
        public CompactParticipantDictionaryEntry(ushort participantRef, string displayName, string vehicleName, string className)
        {
            ParticipantRef = participantRef;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            VehicleName = vehicleName ?? throw new ArgumentNullException(nameof(vehicleName));
            ClassName = className ?? throw new ArgumentNullException(nameof(className));
        }

        public ushort ParticipantRef { get; }
        public string DisplayName { get; }
        public string VehicleName { get; }
        public string ClassName { get; }
    }

    public sealed class CompactStringDictionaryEntry
    {
        public CompactStringDictionaryEntry(CompactStringDictionaryId dictionaryId, uint valueRef, string value)
        {
            if (!Enum.IsDefined(typeof(CompactStringDictionaryId), dictionaryId))
            {
                throw new ArgumentOutOfRangeException(nameof(dictionaryId));
            }
            DictionaryId = dictionaryId;
            ValueRef = valueRef;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public CompactStringDictionaryId DictionaryId { get; }
        public uint ValueRef { get; }
        public string Value { get; }
    }

    public sealed class CompactTelemetrySample
    {
        public CompactTelemetrySample(long elapsedMs, IEnumerable<double?> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            ElapsedMs = elapsedMs;
            Values = Array.AsReadOnly(values.ToArray());
        }

        public long ElapsedMs { get; }
        public IReadOnlyList<double?> Values { get; }
    }

    public sealed class CompactTelemetryBlock
    {
        public CompactTelemetryBlock(
            CompactTelemetrySchemaId schemaId,
            long baseElapsedMs,
            uint cadenceMs,
            IEnumerable<CompactTelemetrySample> samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            CompactTelemetrySchemaRegistry.Get(schemaId);
            CompactTelemetrySample[] copied = samples.ToArray();
            if (copied.Length > CompactTelemetryProtocol.MaximumSamplesPerBlock)
            {
                throw new ArgumentOutOfRangeException(nameof(samples));
            }
            SchemaId = schemaId;
            BaseElapsedMs = baseElapsedMs;
            CadenceMs = cadenceMs;
            Samples = Array.AsReadOnly(copied);
        }

        public CompactTelemetrySchemaId SchemaId { get; }
        public long BaseElapsedMs { get; }
        public uint CadenceMs { get; }
        public IReadOnlyList<CompactTelemetrySample> Samples { get; }

        public static CompactTelemetryBlock FromRows(
            CompactTelemetrySchemaId schemaId,
            long baseElapsedMs,
            uint cadenceMs,
            IEnumerable<IReadOnlyList<double?>> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var samples = new List<CompactTelemetrySample>();
            foreach (IReadOnlyList<double?> row in rows)
            {
                long elapsed = checked(baseElapsedMs + ((long)samples.Count * cadenceMs));
                samples.Add(new CompactTelemetrySample(elapsed, row));
            }
            return new CompactTelemetryBlock(schemaId, baseElapsedMs, cadenceMs, samples);
        }

        // Projects a P023 field-catalog row into an immutable V1 schema by name. Field names
        // are used only at this conversion boundary; the binary payload contains ordinals only.
        public static CompactTelemetryBlock FromCatalogRows(
            CompactTelemetrySchemaId schemaId,
            long baseElapsedMs,
            uint cadenceMs,
            IReadOnlyList<string> sourceFields,
            IEnumerable<IReadOnlyList<double?>> sourceRows)
        {
            if (sourceFields == null) throw new ArgumentNullException(nameof(sourceFields));
            if (sourceRows == null) throw new ArgumentNullException(nameof(sourceRows));

            var sourceOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < sourceFields.Count; index++)
            {
                if (!sourceOrdinals.TryAdd(sourceFields[index], index))
                {
                    throw new ArgumentException("Source field names must be unique.", nameof(sourceFields));
                }
            }

            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(schemaId);
            int[] projection = new int[schema.Fields.Count];
            for (int index = 0; index < schema.Fields.Count; index++)
            {
                if (!sourceOrdinals.TryGetValue(schema.Fields[index].Name, out projection[index]))
                {
                    throw new ArgumentException(
                        "Source catalog does not contain required field " + schema.Fields[index].Name + ".",
                        nameof(sourceFields));
                }
            }

            var projectedRows = new List<IReadOnlyList<double?>>();
            foreach (IReadOnlyList<double?> sourceRow in sourceRows)
            {
                if (sourceRow.Count != sourceFields.Count)
                {
                    throw new ArgumentException("A source row does not match the source catalog.", nameof(sourceRows));
                }
                var projected = new double?[projection.Length];
                for (int index = 0; index < projection.Length; index++) projected[index] = sourceRow[projection[index]];
                projectedRows.Add(projected);
            }

            return FromRows(schemaId, baseElapsedMs, cadenceMs, projectedRows);
        }
    }

    public sealed class CompactTelemetryEnvelope
    {
        public CompactTelemetryEnvelope(
            uint sessionLocalId,
            uint attemptLocalId,
            uint chunkSequence,
            CompactTelemetryBlock block,
            IEnumerable<CompactParticipantDictionaryEntry>? participants = null,
            IEnumerable<CompactStringDictionaryEntry>? strings = null)
        {
            Block = block ?? throw new ArgumentNullException(nameof(block));
            CompactParticipantDictionaryEntry[] copied = participants == null
                ? Array.Empty<CompactParticipantDictionaryEntry>()
                : participants.ToArray();
            if (copied.Length > CompactTelemetryProtocol.MaximumParticipants)
            {
                throw new ArgumentOutOfRangeException(nameof(participants));
            }
            CompactStringDictionaryEntry[] copiedStrings = strings == null
                ? Array.Empty<CompactStringDictionaryEntry>()
                : strings.ToArray();
            if (copiedStrings.Length > CompactTelemetryProtocol.MaximumStringDictionaryEntries)
            {
                throw new ArgumentOutOfRangeException(nameof(strings));
            }

            SessionLocalId = sessionLocalId;
            AttemptLocalId = attemptLocalId;
            ChunkSequence = chunkSequence;
            Participants = new ReadOnlyCollection<CompactParticipantDictionaryEntry>(copied);
            Strings = new ReadOnlyCollection<CompactStringDictionaryEntry>(copiedStrings);
        }

        public uint SessionLocalId { get; }
        public uint AttemptLocalId { get; }
        public uint ChunkSequence { get; }
        public CompactTelemetryBlock Block { get; }
        public IReadOnlyList<CompactParticipantDictionaryEntry> Participants { get; }
        public IReadOnlyList<CompactStringDictionaryEntry> Strings { get; }
    }
}
