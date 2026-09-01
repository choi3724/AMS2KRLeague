using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueActivity.Tests
{
    internal sealed class ActivityFixtureDocument
    {
        public string Schema { get; set; } = string.Empty;
        public string Scenario { get; set; } = string.Empty;
        public List<ActivityFixtureSnapshot> Snapshots { get; set; } = new List<ActivityFixtureSnapshot>();
    }

    internal sealed class ActivityFixtureSnapshot
    {
        public DateTimeOffset CapturedAtUtc { get; set; }
        public string GameState { get; set; } = "InGamePlaying";
        public string SessionState { get; set; } = "Race";
        public string RaceState { get; set; } = "Racing";
        public int ViewedParticipantIndex { get; set; }
        public uint LapsInEvent { get; set; }
        public bool LapInvalidated { get; set; }
        public float CurrentTime { get; set; }
        public float EventTimeRemaining { get; set; } = -1;
        public float SessionDuration { get; set; }
        public string Track { get; set; } = "Bathurst";
        public string Layout { get; set; } = "2020";
        public float AmbientTemperature { get; set; } = 25;
        public float TrackTemperature { get; set; } = 30;
        public float RainDensity { get; set; }
        public float WindSpeed { get; set; } = 2;
        public float WindDirectionX { get; set; }
        public float WindDirectionY { get; set; }
        public float CloudBrightness { get; set; }
        public float SnowDensity { get; set; }
        public int EnforcedPitStopLap { get; set; } = -1;
        public bool SessionIsPrivate { get; set; }
        public List<ActivityFixtureParticipant> Participants { get; set; } = new List<ActivityFixtureParticipant>();

        public TelemetrySnapshot ToSnapshot(uint sequenceNumber)
        {
            ParticipantSnapshot[] participants = Participants
                .Select((participant, index) => participant.ToParticipant(index))
                .ToArray();
            ActivityFixtureParticipant? local = ViewedParticipantIndex >= 0 && ViewedParticipantIndex < Participants.Count
                ? Participants[ViewedParticipantIndex]
                : null;
            return new TelemetrySnapshot(
                CapturedAtUtc,
                SharedMemoryLayout.SupportedVersion,
                3398,
                sequenceNumber,
                (uint)ParseEnum<AMS2LeagueClient.Core.Telemetry.GameState>(GameState),
                (uint)ParseEnum<AMS2LeagueClient.Core.Telemetry.SessionState>(SessionState),
                (uint)ParseEnum<AMS2LeagueClient.Core.Telemetry.RaceState>(RaceState),
                ViewedParticipantIndex,
                participants.Length,
                LapsInEvent,
                local?.LastLapTime ?? -1,
                local?.BestLapTime ?? -1,
                -1,
                -1,
                participants,
                trackLocation: Track,
                trackVariation: Layout,
                numSectors: 3,
                lapInvalidated: LapInvalidated,
                currentTime: CurrentTime,
                currentSector1Time: local?.Sector1 ?? -1,
                currentSector2Time: local?.Sector2 ?? -1,
                currentSector3Time: local?.Sector3 ?? -1,
                trackLength: 6213,
                eventTimeRemaining: EventTimeRemaining,
                sessionDuration: SessionDuration,
                rootCarName: local?.Vehicle ?? string.Empty,
                rootCarClassName: local?.VehicleClass ?? string.Empty,
                ambientTemperature: AmbientTemperature,
                trackTemperature: TrackTemperature,
                rainDensity: RainDensity,
                windSpeed: WindSpeed,
                windDirectionX: WindDirectionX,
                windDirectionY: WindDirectionY,
                cloudBrightness: CloudBrightness,
                snowDensity: SnowDensity,
                enforcedPitStopLap: EnforcedPitStopLap,
                sessionIsPrivate: SessionIsPrivate);
        }

        private static T ParseEnum<T>(string value) where T : struct
        {
            if (!Enum.TryParse(value, true, out T parsed))
            {
                throw new InvalidDataException("Unknown fixture enum " + typeof(T).Name + ": " + value);
            }
            return parsed;
        }
    }

    internal sealed class ActivityFixtureParticipant
    {
        public bool Active { get; set; } = true;
        public string Name { get; set; } = "Fixture Driver";
        public uint Position { get; set; } = 1;
        public uint LapsCompleted { get; set; }
        public uint CurrentLap { get; set; } = 1;
        public string RaceState { get; set; } = "Racing";
        public string PitMode { get; set; } = "None";
        public float BestLapTime { get; set; } = -1;
        public float LastLapTime { get; set; } = -1;
        public string Vehicle { get; set; } = "GT3 Fixture";
        public string VehicleClass { get; set; } = "GT3";
        public bool LapInvalidated { get; set; }
        public float Sector1 { get; set; } = -1;
        public float Sector2 { get; set; } = -1;
        public float Sector3 { get; set; } = -1;

        public ParticipantSnapshot ToParticipant(int index)
            => new ParticipantSnapshot(
                index,
                Active,
                Name,
                Position,
                LapsCompleted,
                CurrentLap,
                1,
                (uint)ParseRaceState(RaceState),
                (uint)ParsePitMode(PitMode),
                BestLapTime,
                LastLapTime,
                Vehicle,
                VehicleClass,
                currentLapDistance: 1000 + index,
                lapInvalidated: LapInvalidated,
                currentSector1Time: Sector1,
                currentSector2Time: Sector2,
                currentSector3Time: Sector3);

        private static AMS2LeagueClient.Core.Telemetry.RaceState ParseRaceState(string value)
        {
            if (!Enum.TryParse(value, true, out AMS2LeagueClient.Core.Telemetry.RaceState parsed))
            {
                throw new InvalidDataException("Unknown fixture RaceState: " + value);
            }
            return parsed;
        }

        private static AMS2LeagueClient.Core.Telemetry.PitMode ParsePitMode(string value)
        {
            if (!Enum.TryParse(value, true, out AMS2LeagueClient.Core.Telemetry.PitMode parsed))
            {
                throw new InvalidDataException("Unknown fixture PitMode: " + value);
            }
            return parsed;
        }
    }

    internal static class ActivityFixtureLoader
    {
        private const string ExpectedSchema = "ams2-phase1d3-activity-fixture-v1";
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static ActivityFixtureDocument Load(string fileName)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "phase1d3_activity", fileName);
            ActivityFixtureDocument? fixture = JsonSerializer.Deserialize<ActivityFixtureDocument>(File.ReadAllBytes(path), Options);
            if (fixture == null) throw new InvalidDataException("Fixture was null: " + fileName);
            if (!string.Equals(ExpectedSchema, fixture.Schema, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Fixture schema mismatch: " + fileName);
            }
            if (fixture.Snapshots.Count == 0) throw new InvalidDataException("Fixture has no snapshots: " + fileName);
            return fixture;
        }

        public static IReadOnlyList<TelemetrySnapshot> LoadSnapshots(string fileName)
        {
            ActivityFixtureDocument fixture = Load(fileName);
            return fixture.Snapshots.Select((snapshot, index) => snapshot.ToSnapshot((uint)(200 + (index * 2)))).ToArray();
        }

        public static ActivityCaptureUpdate Replay(ActivityCaptureEngine engine, string fileName)
        {
            var combined = new ActivityCaptureUpdate();
            foreach (TelemetrySnapshot snapshot in LoadSnapshots(fileName))
            {
                ActivityLocalParticipantResolution local = new ActivityLocalParticipantResolver().Resolve(snapshot);
                ActivityCaptureUpdate update = engine.Observe(snapshot, local.IsValid ? local.Participant : null);
                combined.CompletedRecords.AddRange(update.CompletedRecords);
                combined.Events.AddRange(update.Events);
            }
            return combined;
        }
    }
}
