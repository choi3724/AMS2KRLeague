namespace AMS2LeagueClient.Core.Telemetry
{
    public enum GameState : uint
    {
        Exited = 0,
        FrontEnd = 1,
        InGamePlaying = 2,
        InGamePaused = 3,
        InGameMenuTimeTicking = 4,
        InGameRestarting = 5,
        InGameReplay = 6,
        FrontEndReplay = 7
    }

    public enum SessionState : uint
    {
        Invalid = 0,
        Practice = 1,
        Test = 2,
        Qualify = 3,
        FormationLap = 4,
        Race = 5,
        TimeAttack = 6
    }

    public enum RaceState : uint
    {
        Invalid = 0,
        NotStarted = 1,
        Racing = 2,
        Finished = 3,
        Disqualified = 4,
        Retired = 5,
        Dnf = 6
    }

    public enum PitMode : uint
    {
        None = 0,
        DrivingIntoPits = 1,
        InPit = 2,
        DrivingOutOfPits = 3,
        InGarage = 4,
        DrivingOutOfGarage = 5
    }

    public enum PitSchedule : uint
    {
        None = 0,
        PlayerRequested = 1,
        EngineerRequested = 2,
        DamageRequested = 3,
        Mandatory = 4,
        DriveThrough = 5,
        StopGo = 6,
        PitSpotOccupied = 7
    }

    public enum FlagColour : uint
    {
        None = 0,
        Green = 1,
        Blue = 2,
        WhiteSlowCar = 3,
        WhiteFinalLap = 4,
        Red = 5,
        Yellow = 6,
        DoubleYellow = 7,
        BlackAndWhite = 8,
        BlackOrangeCircle = 9,
        Black = 10,
        Chequered = 11
    }

    public enum FlagReason : uint
    {
        None = 0,
        SoloCrash = 1,
        VehicleCrash = 2,
        VehicleObstruction = 3
    }

    /// <summary>
    /// AMS2 Shared Memory v14 mYellowFlagState. Unlike mHighestFlagColour,
    /// this is the authoritative Full Course Yellow / safety-car procedure state.
    /// </summary>
    public enum YellowFlagState : int
    {
        Invalid = -1,
        None = 0,
        Pending = 1,
        PitsClosed = 2,
        PitLeadLap = 3,
        PitsOpen = 4,
        PitsOpen2 = 5,
        LastLap = 6,
        Resume = 7,
        RaceHalt = 8
    }

    public enum GapSource
    {
        GameSplit,
        Estimated,
        LapDelta,
        Status,
        Unknown
    }

    public enum TelemetryReadStatus
    {
        Success,
        MappingUnavailable,
        InconsistentSnapshot,
        UnsupportedVersion,
        InvalidData,
        Error
    }
}
