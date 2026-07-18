using NUnit.Framework;
using Ryujinx.Input.Tomodachi.Ipc;
using System;
using System.IO;

namespace Ryujinx.Input.Tests.Tomodachi
{
    [TestFixture]
    public class TomodachiStatusProofAuthorityTests
    {
        [Test]
        public void ProofFailsClosedUntilConfiguredSaveIsBoundToEmulatorSaveAuthority()
        {
            string actualSavePath = Directory.CreateTempSubdirectory("tomodachi-proof-").FullName;
            string otherSavePath = Directory.CreateTempSubdirectory("tomodachi-other-").FullName;
            try
            {
                ManualTimeProvider time = new(new DateTimeOffset(2026, 7, 18, 20, 0, 0, TimeSpan.Zero));
                using TomodachiStatusProofAuthority authority = new(
                    "C:/private/game/save",
                    actualSavePath,
                    "C:/private/game/session.json",
                    time);

                Assert.Multiple(() =>
                {
                    Assert.That(authority.TrySample(out _), Is.False);
                    Assert.That(authority.TryBind(otherSavePath, () => "paused"), Is.False);
                    Assert.That(authority.TrySample(out _), Is.False);
                });

                Assert.That(authority.TryBind(actualSavePath, () => "paused"), Is.True);
                Assert.That(authority.TrySample(out _), Is.False);
                time.Advance(TimeSpan.FromMilliseconds(250));
                Assert.That(authority.TrySample(out TomodachiStatusProofSnapshot paused), Is.True);
                time.Advance(TimeSpan.FromMilliseconds(10));
                Assert.That(authority.TrySample(out TomodachiStatusProofSnapshot second), Is.True);
                authority.MarkExited();
                Assert.That(authority.TrySample(out TomodachiStatusProofSnapshot beforeExitEligible), Is.True);
                time.Advance(TimeSpan.FromMilliseconds(250));
                Assert.That(authority.TrySample(out TomodachiStatusProofSnapshot exited), Is.True);

                Assert.Multiple(() =>
                {
                    Assert.That(paused.State, Is.EqualTo("paused"));
                    Assert.That(paused.GameSaveIdentityDigest, Is.EqualTo("sha256:d52828ba13f48e54e8b55d03427100dd6203b90cafebd83f7aa67f50fd806c2d"));
                    Assert.That(paused.ProviderEpoch, Is.GreaterThan(0));
                    Assert.That(paused.ObservedAt, Is.EqualTo(new DateTimeOffset(2026, 7, 18, 20, 0, 0, TimeSpan.Zero)));
                    Assert.That(second.ProviderEpoch, Is.EqualTo(paused.ProviderEpoch));
                    Assert.That(second.ObservedAt, Is.EqualTo(paused.ObservedAt));
                    Assert.That(beforeExitEligible.State, Is.EqualTo("paused"));
                    Assert.That(exited.State, Is.EqualTo("exited"));
                    Assert.That(exited.ProviderEpoch, Is.GreaterThan(paused.ProviderEpoch));
                });
            }
            finally
            {
                Directory.Delete(actualSavePath, recursive: true);
                Directory.Delete(otherSavePath, recursive: true);
            }
        }

        [Test]
        public void FailedRebindInvalidatesPreviouslyEligibleExitProof()
        {
            string actualSavePath = Directory.CreateTempSubdirectory("tomodachi-proof-").FullName;
            string otherSavePath = Directory.CreateTempSubdirectory("tomodachi-other-").FullName;
            try
            {
                ManualTimeProvider time = new(new DateTimeOffset(2026, 7, 18, 20, 0, 0, TimeSpan.Zero));
                using TomodachiStatusProofAuthority authority = new("identity", actualSavePath, null, time);

                Assert.That(authority.TryBind(actualSavePath, () => "running"), Is.True);
                authority.MarkExited();
                time.Advance(TimeSpan.FromMilliseconds(250));
                Assert.That(authority.TrySample(out TomodachiStatusProofSnapshot exited), Is.True);
                Assert.That(exited.State, Is.EqualTo("exited"));

                Assert.That(authority.TryBind(otherSavePath, () => "running"), Is.False);
                time.Advance(TimeSpan.FromMilliseconds(500));

                Assert.That(authority.TrySample(out _), Is.False);
            }
            finally
            {
                Directory.Delete(actualSavePath, recursive: true);
                Directory.Delete(otherSavePath, recursive: true);
            }
        }

        [TestCase("")]
        [TestCase("not-canonical")]
        public void NonCanonicalRuntimeStateNeverProducesProof(string state)
        {
            string savePath = Directory.CreateTempSubdirectory("tomodachi-proof-").FullName;
            try
            {
                ManualTimeProvider time = new(new DateTimeOffset(2026, 7, 18, 20, 0, 0, TimeSpan.Zero));
                using TomodachiStatusProofAuthority authority = new("identity", savePath, null, time);
                Assert.That(authority.TryBind(savePath, () => state), Is.True);
                time.Advance(TimeSpan.FromMilliseconds(250));
                Assert.That(authority.TrySample(out _), Is.False);
            }
            finally
            {
                Directory.Delete(savePath, recursive: true);
            }
        }

        private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
        {
            private DateTimeOffset _now = now;

            public override DateTimeOffset GetUtcNow() => _now;

            public void Advance(TimeSpan duration) => _now += duration;
        }
    }
}
