# Privacy and visibility

## Scope

The telemetry archive stores two deliberately separate data classes. Visibility is part of every chunk envelope and server index row; it is not inferred later from a file name.

| Visibility | Intended use | Default readers |
| --- | --- | --- |
| `PUBLIC_REPLAY` | Session facts, race story, participant positions and raw incident-candidate traces | The contributing installation and, after server classification/approval, replay consumers |
| `PRIVATE_DRIVER_ANALYTICS` | The local driver's controls and vehicle/physics telemetry | The owning installation or a server-side driver account that has explicitly claimed it |

`PUBLIC_REPLAY` means that the data is technically eligible for a public race replay. It does not automatically make an unclassified session, a private lobby, or an anonymous upload public. Publication remains a server decision.

## Stream policy

| Stream | Required visibility | Notes |
| --- | --- | --- |
| `SESSION_METADATA` | `PUBLIC_REPLAY` | Contains session facts and a compact participant dictionary, not credentials. Server privacy/classification can still prevent publication. |
| `RACE_STORY` | `PUBLIC_REPLAY` | Detected facts, raw states and event locations. Presentation suppression never deletes evidence. |
| `PARTICIPANT_REPLAY` | `PUBLIC_REPLAY` | Low-rate participant movement and race state. No inferred controls for remote participants. |
| `DRIVER_TELEMETRY` | `PRIVATE_DRIVER_ANALYTICS` | Intended for an attested owner vehicle. The current viewed/root consistency gate is not authoritative ownership, so this stream is release-blocked and should default OFF. |
| `INCIDENT_TRACE` | `PUBLIC_REPLAY` | Bounded raw candidate evidence for involved/nearby cars. It never contains a fault or blame conclusion. |

## Ownership and authorization

Server authorization and Client source authority are separate. The Server correctly binds a private chunk to the uploading installation and prevents another installation from reading it. That protection does not prove that the Client selected the installation owner's car before capture.

Direct inspection of the official v14 header found `mViewedParticipantIndex` but no authoritative spectator, local-owner, or player-ID signal. Game state cannot distinguish spectator-playing from owner-driving, and active input/control values are not identity authority. The current `ActivityLocalParticipantResolver` accepts a matching viewed participant while `InGamePlaying`; it cannot rule out a spectator following a remote car. Therefore the current `LocalParticipantResolved`/participant-reference checks are consistency checks, not ownership proof.

The release-safe minimum is to keep `DRIVER_TELEMETRY` **OFF/fail-closed by default until authoritative attestation exists**. A one-participant session or Time Attack restriction may reduce exposure but remains a heuristic, not ownership proof; if used experimentally it must be explicit, opt-in, and unreleased. Until this gate is closed, private chunks must not be exposed for user analytics or coaching.

- Every private chunk remains bound to its original installation owner at ingest.
- A valid installation credential may upload and read its own private chunks.
- Another installation cannot enumerate or download those chunks.
- If an anonymous installation is later linked to a league/Steam identity, the server may transfer the existing ownership link after an authenticated claim. The telemetry payload does not repeat a Steam ID at 20 Hz.
- Public replay range reads are permitted only when the server has made the parent session visible. Possession of a `chunkId` alone is not authorization.
- Server-side maintenance and reprocessing may read raw archives under an administrative service role. That role is not exposed to the client.

## Data minimization

Chunks must not contain:

- bearer tokens, API secrets or database credentials;
- Windows user names, home-directory paths or machine IP addresses;
- FTP/SSH credentials;
- repeated Steam IDs or other account identifiers in sample rows;
- client-derived fault, blame or misconduct claims.

The common join keys (`sessionId`, `sessionFingerprint`, `witnessId`, `attemptId`) are opaque capture identifiers. Participant names may be present in the public session dictionary because they are required to reconstruct the observed race; they are not repeated in every frame.

## Raw facts and derived analytics

The client captures SHM facts and preserves raw enum/value evidence. Braking-point judgements, coaching advice, incident attribution, track geometry, canonical multi-witness replay and similar interpretations are server/offline-analyzer outputs. Derived products must keep provenance back to their source chunk hashes and analyzer version.

## Retention and deletion

This phase does not implement automatic raw-archive deletion. Completed local and server chunks are immutable and retained until a separately approved retention policy exists. Operational cleanup may remove only temporary/staging files whose durable replacement is hash-verified. A later user-data deletion workflow must remove private ownership data and its raw chunks consistently; it must not silently rewrite shared public race evidence.

## Transport and storage controls

- Upload uses HTTPS and the existing installation bearer credential.
- Canonical JSON is compressed with gzip after local durable commit.
- Both uncompressed-payload SHA-256 and compressed-file SHA-256 are recorded.
- Idempotency is based on immutable chunk identity and content hash.
- The server stores raw compressed files outside the public document tree (or behind an authenticated controller) and exposes range reads through authorization checks only.
- Logs include opaque IDs, sizes and result codes, never credentials or raw private samples.

Session Metadata `raceStory`, `replay`, `driverTelemetry`, and `incidentHighRate` booleans are attempt-local input-observation hints only. They do not prove an atomic local commit, upload, owner correctness, or durable availability. Web availability must use only the Server session index response with `capabilitySource=DURABLE_CHUNK_INDEX` and its visibility-aware `streamCapabilities` object.

Current capture completeness also has a release-blocking accounting gap: outer Runtime batch-queue drops and worker failures are not fully propagated into per-stream chunk quality/session completeness. A `COMPLETE` marker or zero inner drop count must not be presented as end-to-end completeness until that propagation is implemented.

## Incident wording

An incident trace means only that a bounded trigger observed a collision magnitude, abrupt state/position change, participant disappearance or proximity pattern. User-facing systems must label it as an “incident candidate” or “possible collision” until an authorized review establishes more. The archive never encodes who was at fault.

The candidate collector adds at most four participants within 50 m of trigger-related world-position anchors while respecting the overall eight-participant cap. A unit fixture verifies inclusion of a near participant and exclusion of a far participant; a real multiplayer incident remains unvalidated.

Overall P023 remains **YELLOW/HOLD** and this candidate was not deployed to production.
