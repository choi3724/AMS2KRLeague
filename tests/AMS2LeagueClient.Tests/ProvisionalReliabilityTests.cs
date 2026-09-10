using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AMS2LeagueClient.Core.ActivityCapture;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void DelayedModeFinalizesOnce()
        {
            WithTemporaryDirectory(directory => {
                string root = Path.Combine(directory, "provisional");
                var store = new ProvisionalActivityStore(root);
                var queue = new ActivityUploadQueue(Path.Combine(directory, "queue"));
                byte[] Capture(string mode, string id = "late-activity") => JsonSerializer.SerializeToUtf8Bytes(new {
                    schema = "ams2-player-activity-v2", activityId = id, raceMode = mode,
                    startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3), endedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
                });
                byte[] original = Capture("UNKNOWN");
                bool identityRejected = false;
                try { store.Stage("wrong-id", "v1/player/activities", "bad-identity", original); }
                catch (InvalidDataException) { identityRejected = true; }
                AssertTrue(identityRejected);
                // A parseable but corrupt local candidate cannot starve valid siblings.
                var malformed = System.Text.Json.Nodes.JsonNode.Parse(original)!;
                malformed["startedAtUtc"] = "not-a-date";
                byte[] badTime = JsonSerializer.SerializeToUtf8Bytes(malformed);
                string broken = Path.Combine(root, "corrupt-local-candidate");
                Directory.CreateDirectory(broken);
                System.IO.File.WriteAllBytes(Path.Combine(broken, "capture.json"), JsonSerializer.SerializeToUtf8Bytes(
                    new ProvisionalActivityStore.Candidate { Id="late-activity", Endpoint="v1/player/activities",
                        Key="bad-time", Payload=badTime, Sha256=ActivityCanonicalSerializer.Sha256(badTime) }));
                store.Stage("late-activity", "v1/player/activities", "late-mode-stable-key", original);
                store.Reconcile(queue, (a,b) => SessionPlayMode.Unknown, (a,b) => false);
                AssertEqual(0, queue.Scan().Count);
                store = new ProvisionalActivityStore(root);
                store.Reconcile(queue, (a,b) => SessionPlayMode.Multiplayer, (a,b) => true);
                var sent = queue.Scan().Single();
                AssertEqual("MULTIPLAYER", JsonDocument.Parse(sent.PayloadUtf8).RootElement.GetProperty("raceMode").GetString()!);
                string hash = sent.Metadata.BodySha256;
                // Simulate a crash after enqueue but before completion marker.
                File.Delete(Directory.GetFiles(root, "queued", SearchOption.AllDirectories).Single());
                new ProvisionalActivityStore(root).Reconcile(queue, (a,b) => SessionPlayMode.Unknown, (a,b) => false);
                AssertEqual(1, queue.Scan().Count);
                AssertEqual(hash, queue.Scan().Single().Metadata.BodySha256);
                store.Stage("single-activity", "v1/player/activities", "single-mode-stable-key", Capture("SINGLE_PLAYER", "single-activity"));
                store.Reconcile(queue, (a,b) => SessionPlayMode.Multiplayer, (a,b) => true);
                AssertEqual(1, queue.Scan().Count);
            });
        }
    }
}
