# Local Driver Telemetry Contract

작성 기준: 2026-09-02 KST
stream: `DRIVER_TELEMETRY`
visibility: `PRIVATE_DRIVER_ANALYTICS`
row schema: **222 fields = immutable 52-field prefix + append-only 82 scalar fields + append-only 88 four-wheel fields**

## 1. Authority and privacy boundary

This stream is intended to record only the client owner's local/root vehicle. The candidate is emitted only when the current resolver, viewed/root source match, local-resolution flag, and participant reference agree. Those checks prove internal viewed/root consistency, not ownership. Direct inspection of the official v14 header found `mViewedParticipantIndex` but no authoritative spectator, local-owner, or player-ID signal. Game state cannot distinguish spectator-playing from owner-driving, and input/control activity is not identity authority. A spectator following a remote car may therefore satisfy the current `InGamePlaying` resolver. The 1-person Practice and fixtures verify row generation/shape, not source ownership.

It is private driver analytics by default. Public replay and League evidence streams never receive these control, powertrain, tyre, or suspension fields. Server owner-bound visibility prevents cross-installation reads but cannot repair a wrongly selected Client source. The release-safe minimum is `DRIVER_TELEMETRY` **OFF/fail-closed by default until authoritative attestation exists**. One-participant or Time Attack allowance is only a risk-reducing heuristic, not ownership proof, and must not be treated as a release gate. `null` is not zero: it denotes an unavailable/non-finite/unexposed source value. Raw enums and raw values must not be interpreted as a sporting decision by the client.

## 2. Compatibility and encoding

`data.fields` is the authoritative positional schema for every chunk. The first 52 cells are the v1 prefix and remain unchanged in name, order, and interpretation. The later 170 cells are append-only. A legacy reader may safely consume exactly the 52-cell prefix and ignore its suffix.

All row values are numeric/null except the four `tyreCompound*Ref` values, which are optional indices into `dictionaries.tyreCompounds`. No control or physics value is repeated in a public dictionary.

## 3. v1 prefix (0–51)

| indices | fields | unit / rule |
|---:|---|---|
| 0–4 | `sessionElapsedMs`, `capturedAtUnixMs`, `driverRef`, `lap`, `sector` | monotonic ms, UTC evidence ms, reference, observed ordinals |
| 5–8 | `lapDistanceMeters`, `worldX`, `worldY`, `worldZ` | observed metres and raw world-coordinate components |
| 9–11 | `speedMetersPerSecond`, `rpm`, `gearRaw` | m/s where named; RPM; raw gear enum/value |
| 12–19 | `throttle`, `brake`, `steering`, `clutch`, `unfilteredThrottle`, `unfilteredBrake`, `unfilteredSteering`, `unfilteredClutch` | source normalized input channels; filtered and unfiltered channels are distinct facts |
| 20–22 | `longitudinalAccelerationMetersPerSecondSquared`, `lateralAccelerationMetersPerSecondSquared`, `verticalAccelerationMetersPerSecondSquared` | normalized candidate acceleration axes; axis semantics remain pending real validation |
| 23–26 | `headingRadians`, `velocityX`, `velocityY`, `velocityZ` | heading is orientation-Y projection pending convention validation; velocity components retain source axis convention |
| 27–29 | `fuelLevelRatio`, `fuelCapacityLiters`, `fuelLiters` | raw ratio, header-declared litre capacity, and `ratio × capacity` derived candidate; historical pre-fix fuel values must not be retrospectively called litres |
| 30–33 | `brakeBias`, `engineDamage`, `aeroDamage`, `suspensionDamage` | source scalars, no fault inference |
| 34–37 | `tyreTempFrontLeftCelsius`, `tyreTempFrontRightCelsius`, `tyreTempRearLeftCelsius`, `tyreTempRearRightCelsius` | source Celsius values |
| 38–41 | `tyrePressureFrontLeftKpa`, `tyrePressureFrontRightKpa`, `tyrePressureRearLeftKpa`, `tyrePressureRearRightKpa` | converted candidate kPa; pressure scale remains `SEMANTICS_PENDING` until real validation |
| 42–45 | `tyreWearFrontLeft`, `tyreWearFrontRight`, `tyreWearRearLeft`, `tyreWearRearRight` | source values; direction/scale is not assumed |
| 46–51 | `trackTemperatureCelsius`, `ambientTemperatureCelsius`, `rainDensity`, `pitStateRaw`, `lapValid`, `currentLapTimeMs` | observed environmental values; raw pit enum; 0/1/null validity; current-lap milliseconds |

## 4. Scalar extension (52–133, 82 fields)

These cells preserve root/participant timing, raw vehicle state, and raw vectors. A field ending in `Raw` is intentionally not translated. A field with a physical unit in its name retains that source unit; unlabelled raw scalars remain `SEMANTICS_PENDING` until explicitly validated.

| group | exact append-only fields |
|---|---|
| invalidation and timing | `rootLapInvalidated`, `participantLapInvalidated`, `bestLapTimeSeconds`, `lastLapTimeSeconds`, `splitTimeAheadSeconds`, `splitTimeBehindSeconds`, `splitTimeSeconds`, `personalFastestLapTimeSeconds`, `worldFastestLapTimeSeconds`, `currentSector1TimeSeconds`, `currentSector2TimeSeconds`, `currentSector3TimeSeconds`, `fastestSector1TimeSeconds`, `fastestSector2TimeSeconds`, `fastestSector3TimeSeconds`, `personalFastestSector1TimeSeconds`, `personalFastestSector2TimeSeconds`, `personalFastestSector3TimeSeconds`, `worldFastestSector1TimeSeconds`, `worldFastestSector2TimeSeconds`, `worldFastestSector3TimeSeconds` |
| flags, pit, engine and collision | `rootPitModeRaw`, `rootPitScheduleRaw`, `participantPitScheduleRaw`, `highestFlagColourRaw`, `highestFlagReasonRaw`, `participantHighestFlagColourRaw`, `participantHighestFlagReasonRaw`, `carFlagsRaw`, `oilTemperatureCelsius`, `oilPressureKPa`, `waterTemperatureCelsius`, `waterPressureKPa`, `fuelPressureKPa`, `maxRpm`, `numGears`, `odometerKilometres`, `antiLockActive`, `lastOpponentCollisionIndex`, `lastOpponentCollisionMagnitude`, `boostActive`, `boostAmount` |
| orientation and motion vectors | `orientationRawX`, `orientationRawY`, `orientationRawZ`, `localVelocityRawX`, `localVelocityRawY`, `localVelocityRawZ`, `worldVelocityRawX`, `worldVelocityRawY`, `worldVelocityRawZ`, `angularVelocityRawX`, `angularVelocityRawY`, `angularVelocityRawZ`, `localAccelerationRawX`, `localAccelerationRawY`, `localAccelerationRawZ`, `worldAccelerationRawX`, `worldAccelerationRawY`, `worldAccelerationRawZ`, `extentsCentreRawX`, `extentsCentreRawY`, `extentsCentreRawZ` |
| vehicle systems and attempt clock | `engineSpeedRadiansPerSecond`, `engineTorqueNewtonMetres`, `frontWingRaw`, `rearWingRaw`, `handBrake`, `crashStateRaw`, `turboBoostPressure`, `drsStateRaw`, `antiLockSetting`, `tractionControlSetting`, `ersDeploymentModeRaw`, `ersAutoModeEnabled`, `clutchTemperatureKelvin`, `clutchWear`, `clutchOverheated`, `clutchSlipping`, `launchStageRaw`, `currentTimeSecondsRaw`, `sequenceNumberRaw` |

`best/last/fastest` values are observed source timings, not an official classification. Collision, crash, DRS, ERS, pit, flag, and assist fields are raw state/evidence; they do not establish causality, eligibility, or penalty.

## 5. Four-wheel extension (134–221, 22 fields × FL/FR/RL/RR)

For each prefix below, exactly four fields occur in this order: `FrontLeft`, `FrontRight`, `RearLeft`, `RearRight` (for example `tyreFlagsFrontLeft` … `tyreFlagsRearRight`). `tyreCompound` alone uses the suffix `Ref` because it indexes `dictionaries.tyreCompounds`.

| per-wheel prefix | source representation / caveat |
|---|---|
| `tyreFlags` | raw integer flags |
| `tyreTerrain` | raw terrain enum/integer |
| `tyreLocalY`, `wheelLocalPositionY` | raw source coordinate components; no cross-axis assumption |
| `tyreRevolutionsPerSecond` | source revolutions/second |
| `tyreHeightAboveGround` | source distance scale pending validation |
| `tyreBrakeDamage`, `tyreSuspensionDamage` | source damage scalars |
| `tyreBrakeTemperatureCelsius` | source Celsius |
| `tyreTreadTemperatureKelvin`, `tyreLayerTemperatureKelvin`, `tyreCarcassTemperatureKelvin`, `tyreRimTemperatureKelvin`, `tyreInternalAirTemperatureKelvin` | source Kelvin |
| `tyreSuspensionTravelMetres` | source metres |
| `tyreSuspensionVelocity` | source value; dimensional semantics pending validation |
| `tyreAirPressurePsi` | source PSI; retained alongside the legacy converted-kPa prefix values |
| `tyreCompoundRef` | index into chunk `tyreCompounds` dictionary; null means absent string |
| `tyreLeftTemperatureCelsius`, `tyreCenterTemperatureCelsius`, `tyreRightTemperatureCelsius` | source Celsius tread values |
| `rideHeightCentimetres` | source centimetres |

The exact 88 appended names, in storage order, are:

| source family | exact `FrontLeft`, `FrontRight`, `RearLeft`, `RearRight` fields |
|---|---|
| flags / terrain | `tyreFlagsFrontLeft`, `tyreFlagsFrontRight`, `tyreFlagsRearLeft`, `tyreFlagsRearRight`; `tyreTerrainFrontLeft`, `tyreTerrainFrontRight`, `tyreTerrainRearLeft`, `tyreTerrainRearRight` |
| local / wheel motion | `tyreLocalYFrontLeft`, `tyreLocalYFrontRight`, `tyreLocalYRearLeft`, `tyreLocalYRearRight`; `tyreRevolutionsPerSecondFrontLeft`, `tyreRevolutionsPerSecondFrontRight`, `tyreRevolutionsPerSecondRearLeft`, `tyreRevolutionsPerSecondRearRight` |
| geometry / damage | `tyreHeightAboveGroundFrontLeft`, `tyreHeightAboveGroundFrontRight`, `tyreHeightAboveGroundRearLeft`, `tyreHeightAboveGroundRearRight`; `tyreBrakeDamageFrontLeft`, `tyreBrakeDamageFrontRight`, `tyreBrakeDamageRearLeft`, `tyreBrakeDamageRearRight`; `tyreSuspensionDamageFrontLeft`, `tyreSuspensionDamageFrontRight`, `tyreSuspensionDamageRearLeft`, `tyreSuspensionDamageRearRight` |
| brake and tyre temperatures | `tyreBrakeTemperatureCelsiusFrontLeft`, `tyreBrakeTemperatureCelsiusFrontRight`, `tyreBrakeTemperatureCelsiusRearLeft`, `tyreBrakeTemperatureCelsiusRearRight`; `tyreTreadTemperatureKelvinFrontLeft`, `tyreTreadTemperatureKelvinFrontRight`, `tyreTreadTemperatureKelvinRearLeft`, `tyreTreadTemperatureKelvinRearRight`; `tyreLayerTemperatureKelvinFrontLeft`, `tyreLayerTemperatureKelvinFrontRight`, `tyreLayerTemperatureKelvinRearLeft`, `tyreLayerTemperatureKelvinRearRight`; `tyreCarcassTemperatureKelvinFrontLeft`, `tyreCarcassTemperatureKelvinFrontRight`, `tyreCarcassTemperatureKelvinRearLeft`, `tyreCarcassTemperatureKelvinRearRight`; `tyreRimTemperatureKelvinFrontLeft`, `tyreRimTemperatureKelvinFrontRight`, `tyreRimTemperatureKelvinRearLeft`, `tyreRimTemperatureKelvinRearRight`; `tyreInternalAirTemperatureKelvinFrontLeft`, `tyreInternalAirTemperatureKelvinFrontRight`, `tyreInternalAirTemperatureKelvinRearLeft`, `tyreInternalAirTemperatureKelvinRearRight` |
| suspension / pressure | `wheelLocalPositionYFrontLeft`, `wheelLocalPositionYFrontRight`, `wheelLocalPositionYRearLeft`, `wheelLocalPositionYRearRight`; `tyreSuspensionTravelMetresFrontLeft`, `tyreSuspensionTravelMetresFrontRight`, `tyreSuspensionTravelMetresRearLeft`, `tyreSuspensionTravelMetresRearRight`; `tyreSuspensionVelocityFrontLeft`, `tyreSuspensionVelocityFrontRight`, `tyreSuspensionVelocityRearLeft`, `tyreSuspensionVelocityRearRight`; `tyreAirPressurePsiFrontLeft`, `tyreAirPressurePsiFrontRight`, `tyreAirPressurePsiRearLeft`, `tyreAirPressurePsiRearRight` |
| compound / tread / ride height | `tyreCompoundFrontLeftRef`, `tyreCompoundFrontRightRef`, `tyreCompoundRearLeftRef`, `tyreCompoundRearRightRef`; `tyreLeftTemperatureCelsiusFrontLeft`, `tyreLeftTemperatureCelsiusFrontRight`, `tyreLeftTemperatureCelsiusRearLeft`, `tyreLeftTemperatureCelsiusRearRight`; `tyreCenterTemperatureCelsiusFrontLeft`, `tyreCenterTemperatureCelsiusFrontRight`, `tyreCenterTemperatureCelsiusRearLeft`, `tyreCenterTemperatureCelsiusRearRight`; `tyreRightTemperatureCelsiusFrontLeft`, `tyreRightTemperatureCelsiusFrontRight`, `tyreRightTemperatureCelsiusRearLeft`, `tyreRightTemperatureCelsiusRearRight`; `rideHeightCentimetresFrontLeft`, `rideHeightCentimetresFrontRight`, `rideHeightCentimetresRearLeft`, `rideHeightCentimetresRearRight` |

## 6. Cadence, processing, and release state

Target cadence is 20 Hz (50 ms), normally up to about 600 private rows per 30-second chunk. Quality records expected/actual/missing/dropped samples and input messages known to the inner archive. Outer Runtime batch-queue drops and worker failures are not yet fully propagated into chunk quality/session completeness, so zero drop or an orderly end marker is not end-to-end completeness proof. The server may later calculate braking points, line variance, coaching, comparisons, and trends from raw rows only after privacy authority and completeness gates are closed; the client writes none of those conclusions.

Fixtures verify positional width 222, prefix compatibility, Server private visibility, dictionaries, gzip/hash, and the viewed/root mismatch gate. They do not verify authoritative local ownership. A short real AMS2 practice run verified persisted core-value changes, but did not validate spectator remote-follow rejection, the expanded physics fields, complete-lap analysis, tyre pressure/wear semantics, heading/acceleration axes, multi-car cases, or 60-minute overhead. The archive/release remains **HOLD/YELLOW**, and production was not deployed.
