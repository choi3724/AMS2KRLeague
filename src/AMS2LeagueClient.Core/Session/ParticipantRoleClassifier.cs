using System;
using System.Text;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Session
{
    public enum ParticipantRole
    {
        RacingDriver,
        SafetyCar,
        UnknownNonRacing
    }

    public sealed class ParticipantRoleClassifier
    {
        public ParticipantRole Classify(ParticipantSnapshot participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            return Classify(participant.VehicleName, participant.VehicleClass);
        }

        public ParticipantRole Classify(string? vehicleName, string? vehicleClass)
        {
            string normalizedClass = NormalizeVehicleToken(vehicleClass);
            string normalizedVehicle = NormalizeVehicleToken(vehicleName);

            // AMS2 build 3398 exposes the real Safety Car as class "SafetyCar"
            // and vehicle "Camaro SafetyCar".  Do not use the display name: a
            // user name alone is not strong enough evidence to remove a driver.
            if (string.Equals(normalizedClass, "safetycar", StringComparison.Ordinal)
                || normalizedVehicle.EndsWith("safetycar", StringComparison.Ordinal))
            {
                return ParticipantRole.SafetyCar;
            }

            // Unknown metadata stays in league classification.  False inclusion is
            // diagnosable; false exclusion would silently remove a real competitor.
            return ParticipantRole.RacingDriver;
        }

        public bool IsLeagueDriver(ParticipantSnapshot participant)
            => Classify(participant) != ParticipantRole.SafetyCar;

        public bool IsLeagueDriver(string? vehicleName, string? vehicleClass)
            => Classify(vehicleName, vehicleClass) != ParticipantRole.SafetyCar;

        private static string NormalizeVehicleToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }
    }
}
