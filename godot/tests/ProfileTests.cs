using System;
using System.IO;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    public class ProfileTests
    {
        [Test]
        public void NewProfileGetsAValidDefaultName()
        {
            var p = PlayerProfile.New("a1b2-c3d4", 1000);
            Assert.That(p.Name, Is.EqualTo("Founder A1B2"));
            Assert.That(PlayerProfile.NameProblem(p.Name), Is.Null);
            Assert.That(p.Avatar, Is.EqualTo(PlayerProfile.Avatars[0].Id));
            Assert.That(p.Frame, Is.EqualTo("wood"));
        }

        [TestCase("Ab", false)]
        [TestCase("   Ana   ", true)]
        [TestCase("Ivan  Horvat", true)]
        [TestCase("Šime Ćorić", true)]
        [TestCase("12345", false)]
        [TestCase("Bad<name>", false)]
        [TestCase("AVeryLongFounderName", false)]
        public void NamesAreChecked(string name, bool ok)
        {
            Assert.That(PlayerProfile.NameProblem(name) == null, Is.EqualTo(ok), PlayerProfile.NameProblem(name));
        }

        [Test]
        public void RenameCleansAndCounts()
        {
            var p = PlayerProfile.New("x", 0);
            Assert.That(p.Rename("  Ivan   Horvat "), Is.Null);
            Assert.That(p.Name, Is.EqualTo("Ivan Horvat"));
            Assert.That(p.Renames, Is.EqualTo(1));
            Assert.That(p.Rename("Ivan Horvat"), Is.Null);
            Assert.That(p.Renames, Is.EqualTo(1), "the same name is not a rename");
            Assert.That(p.Rename("x"), Is.Not.Null);
            Assert.That(p.Name, Is.EqualTo("Ivan Horvat"));
        }

        [Test]
        public void FramesUnlockWithMilestones()
        {
            var p = PlayerProfile.New("x", 0);
            Assert.That(p.SetFrame("gold", 2), Is.False);
            Assert.That(p.Frame, Is.EqualTo("wood"));
            Assert.That(p.SetFrame("silver", 2), Is.True);
            Assert.That(p.SetAvatar("nope"), Is.False);
            Assert.That(p.SetAvatar("face_f2"), Is.True);
        }

        [Test]
        public void ProfileRoundTripsAndToleratesJunk()
        {
            var p = PlayerProfile.New("x", 42);
            p.Rename("Mara");
            p.SetAvatar("face_f3");
            p.PlaySeconds = 3600;
            p.Sessions = 7;
            var back = PlayerProfile.Decode(p.Encode());
            Assert.That(back.Name, Is.EqualTo("Mara"));
            Assert.That(back.Avatar, Is.EqualTo("face_f3"));
            Assert.That(back.PlaySeconds, Is.EqualTo(3600));
            Assert.That(back.Sessions, Is.EqualTo(7));
            Assert.That(back.CreatedAt, Is.EqualTo(42));

            Assert.That(PlayerProfile.Decode("not json"), Is.Null);
            var junk = PlayerProfile.Decode("{\"name\":\"<>\",\"avatar\":\"dragon\",\"frame\":\"diamond\",\"renames\":-3}");
            Assert.That(junk.Name, Is.EqualTo("Founder"));
            Assert.That(junk.Avatar, Is.EqualTo(PlayerProfile.Avatars[0].Id));
            Assert.That(junk.Frame, Is.EqualTo("wood"));
            Assert.That(junk.Renames, Is.EqualTo(0));
        }

        [Test]
        public void ProfileFileStore()
        {
            var dir = Path.Combine(Path.GetTempPath(), "founders-profile-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileProfileStore(dir);
                Assert.That(store.Load(), Is.Null);
                var p = PlayerProfile.New("abcd", 1);
                store.Save(p);
                Assert.That(store.Load().Name, Is.EqualTo(p.Name));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ProsperityGrowsWithTheSettlement()
        {
            var (game, clock) = Fixtures.NewGame();
            int before = PlayerProfile.Prosperity(game.State);
            game.AddCompleted("wood_camp", 3, 3);
            game.State.Population += 2;
            Assert.That(PlayerProfile.Prosperity(game.State), Is.GreaterThan(before));
        }
    }

    public class CloudSyncTests
    {
        [Test]
        public void SnapshotRoundTripsAndOpens()
        {
            var (game, clock) = Fixtures.NewGame();
            var profile = PlayerProfile.New(game.State.SettlementId, 5);
            profile.Rename("Lada");
            var snap = CloudSnapshot.Make(game.State, game.Content, profile, "dev-1", "iPhone", 1234);
            var back = CloudSnapshot.Decode(snap.Encode());
            Assert.That(back.UploadId, Is.EqualTo(snap.UploadId));
            Assert.That(back.FounderName, Is.EqualTo("Lada"));
            Assert.That(back.DeviceName, Is.EqualTo("iPhone"));
            Assert.That(back.Population, Is.EqualTo(game.State.Population));

            var (state, p, problem) = CloudSync.Open(back, game.Content);
            Assert.That(problem, Is.Null);
            Assert.That(state.SettlementId, Is.EqualTo(game.State.SettlementId));
            Assert.That(p.Name, Is.EqualTo("Lada"));
        }

        [Test]
        public void DamagedOrNewerSnapshotsAreRefused()
        {
            Assert.That(CloudSnapshot.Decode("{}"), Is.Null);
            Assert.That(CloudSnapshot.Decode("{\"format\":99,\"uploadId\":\"a\",\"save\":\"{}\"}"), Is.Null);
            var (game, _) = Fixtures.NewGame();
            var bad = new CloudSnapshot { UploadId = "x", Save = "{broken" };
            Assert.That(CloudSync.Open(bad, game.Content).problem, Is.Not.Null);
            var newer = new CloudSnapshot { UploadId = "x", Save = "{\"saveVersion\":99,\"state\":{}}" };
            Assert.That(CloudSync.Open(newer, game.Content).problem, Does.Contain("newer"));
        }

        [Test]
        public void TheCloudIsNeverOverwrittenWithoutAsking()
        {
            var cloud = new CloudSnapshot { UploadId = "u1" };
            Assert.That(CloudSync.Decide(null, null), Is.EqualTo(CloudAction.Upload));
            Assert.That(CloudSync.Decide(cloud, "u1"), Is.EqualTo(CloudAction.Upload));
            Assert.That(CloudSync.Decide(cloud, "u0"), Is.EqualTo(CloudAction.Choose), "another device uploaded since");
            Assert.That(CloudSync.Decide(cloud, null), Is.EqualTo(CloudAction.Choose), "a new install finds a cloud save");
        }
    }
}
