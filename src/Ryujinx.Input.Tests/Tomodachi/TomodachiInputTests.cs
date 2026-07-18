using NUnit.Framework;
using Ryujinx.Input.Drivers;
using Ryujinx.Input.Tomodachi;
using System;
using System.Collections.Generic;

namespace Ryujinx.Input.Tests.Tomodachi
{
    [TestFixture]
    public class TomodachiInputTests
    {
        private static readonly TomodachiAuthorityEpoch Authority = new("server", "room", "lease", "session");

        private static TomodachiInputCommand Command(string commandId, long sequence, TomodachiButtonAction action)
        {
            return new TomodachiInputCommand(
                commandId,
                Authority,
                sequence,
                DateTimeOffset.MaxValue,
                GamepadButtonInputId.A,
                action,
                $"trace-{commandId}");
        }

        [Test]
        public void StartsDisabledOrUnarmedAndAllNeutral()
        {
            using TomodachiInputState state = new();
            using EmptyGamepadDriver disabledPrimary = new();
            TomodachiInputBootstrapResult disabled = TomodachiInputBootstrap.CreateGamepadDriver(disabledPrimary);
            TomodachiInputBootstrapResult enabled = TomodachiInputBootstrap.CreateGamepadDriver(
                new EmptyGamepadDriver(),
                enableProvider: true);
            using IGamepadDriver enabledDriver = enabled.GamepadDriver;
            using TomodachiInputState validationState = new();
            ArmResult invalidArm = validationState.Arm(string.Empty, Authority);
            validationState.Arm("arm-valid", Authority);
            ApplyResult invalidCommandId = validationState.Apply(
                Command(string.Empty, 1, TomodachiButtonAction.Press));
            ApplyResult invalidAction = validationState.Apply(
                Command("bad-action", 2, (TomodachiButtonAction)999));

            ProviderHealth health = state.GetHealth();
            PollResult poll = state.PollMappedSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(disabled.GamepadDriver, Is.SameAs(disabledPrimary));
                Assert.That(disabled.InputControl, Is.Null);
                Assert.That(enabled.GamepadDriver, Is.TypeOf<CompositeGamepadDriver>());
                Assert.That(enabled.InputControl, Is.Not.Null);
                Assert.That(enabled.InputControl.GetHealth().Armed, Is.False);
                Assert.That(enabled.InputControl.GetHealth().AllNeutral, Is.True);
                Assert.That(invalidArm.Armed, Is.False);
                Assert.That(invalidArm.Detail, Is.EqualTo("invalid-arm-id"));
                Assert.That(invalidCommandId.Accepted, Is.False);
                Assert.That(invalidCommandId.Receipt.Detail, Is.EqualTo("invalid-command-id"));
                Assert.That(invalidAction.Accepted, Is.False);
                Assert.That(invalidAction.Receipt.Detail, Is.EqualTo("unsupported-action"));
                Assert.That(health.Enabled, Is.True);
                Assert.That(health.Armed, Is.False);
                Assert.That(health.Latched, Is.False);
                Assert.That(health.AllNeutral, Is.True);
                Assert.That(poll.Snapshot.IsPressed(GamepadButtonInputId.A), Is.False);
                Assert.That(poll.AllNeutral, Is.True);
            });
        }

        [Test]
        public void ArmedAPressAppearsOnNextPoll()
        {
            using TomodachiInputState state = new();
            Assert.That(state.Arm("arm-1", Authority).Armed, Is.True);

            ApplyResult applied = state.Apply(Command("press-1", 1, TomodachiButtonAction.Press));
            ArmResult duplicateArm = state.Arm("arm-1", Authority);
            PollResult poll = state.PollMappedSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(applied.Accepted, Is.True);
                Assert.That(duplicateArm.Armed, Is.True);
                Assert.That(duplicateArm.Detail, Is.EqualTo("already-armed"));
                Assert.That(poll.Snapshot.IsPressed(GamepadButtonInputId.A), Is.True);
                Assert.That(poll.AllNeutral, Is.False);
            });
        }

        [Test]
        public void PressAndReleaseQueuedBeforePollProduceTwoDistinctPolls()
        {
            using TomodachiInputState state = new();
            state.Arm("arm-1", Authority);
            state.Apply(Command("press-1", 1, TomodachiButtonAction.Press));
            state.Apply(Command("release-1", 2, TomodachiButtonAction.Release));

            PollResult pressed = state.PollMappedSnapshot();
            PollResult released = state.PollMappedSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(pressed.Snapshot.IsPressed(GamepadButtonInputId.A), Is.True);
                Assert.That(released.Snapshot.IsPressed(GamepadButtonInputId.A), Is.False);
                Assert.That(released.ProviderPoll, Is.EqualTo(pressed.ProviderPoll + 1));
            });
        }

        [Test]
        public void NormalReleaseExposesNpadSampledAndAllNeutralReceipts()
        {
            using TomodachiInputState state = new();
            state.Arm("arm-1", Authority);
            state.Apply(Command("press-1", 1, TomodachiButtonAction.Press));
            PollResult pressed = state.PollMappedSnapshot();
            state.Apply(Command("release-2", 2, TomodachiButtonAction.Release));

            PollResult released = state.PollMappedSnapshot();
            bool foundPress = state.TryGetCommandReceipt("press-1", out CommandReceipt pressReceipt);
            bool foundRelease = state.TryGetCommandReceipt("release-2", out CommandReceipt releaseReceipt);
            NeutralSampleReceipt? lastNeutral = state.GetLastAllNeutralSampleReceipt();

            Assert.Multiple(() =>
            {
                Assert.That(pressed.SampledCommandReceipts, Has.Length.EqualTo(1));
                Assert.That(pressed.SampledCommandReceipts[0].CommandId, Is.EqualTo("press-1"));
                Assert.That(pressed.SampledCommandReceipts[0].Detail, Is.EqualTo("npad-sampled"));
                Assert.That(foundPress, Is.True);
                Assert.That(pressReceipt.ProviderPoll, Is.EqualTo(pressed.ProviderPoll));
                Assert.That(foundRelease, Is.True);
                Assert.That(releaseReceipt.Sampled, Is.True);
                Assert.That(releaseReceipt.ProviderPoll, Is.EqualTo(released.ProviderPoll));
                Assert.That(releaseReceipt.Detail, Is.EqualTo("npad-sampled"));
                Assert.That(released.SampledCommandReceipts, Has.Length.EqualTo(1));
                Assert.That(released.SampledCommandReceipts[0], Is.EqualTo(releaseReceipt));
                Assert.That(released.AllNeutral, Is.True);
                Assert.That(released.AllNeutralSampled, Is.True);
                Assert.That(released.AllNeutralReceipt.HasValue, Is.True);
                Assert.That(released.AllNeutralReceipt.Value.AllNeutral, Is.True);
                Assert.That(released.AllNeutralReceipt.Value.ProviderPoll, Is.EqualTo(released.ProviderPoll));
                Assert.That(released.AllNeutralReceipt.Value.SampledAt, Is.EqualTo(released.SampledAt));
                Assert.That(released.AllNeutralReceipt.Value.NeutralGeneration, Is.GreaterThan(0));
                Assert.That(lastNeutral, Is.EqualTo(released.AllNeutralReceipt));
            });
        }

        [Test]
        public void DuplicateCommandIdReturnsSameReceiptWithoutSecondEdge()
        {
            using TomodachiInputState state = new(maxPendingTransitions: 2, maxCommandResults: 2);
            state.Arm("arm-1", Authority);
            TomodachiInputCommand command = Command("press-1", 1, TomodachiButtonAction.Press);

            ApplyResult first = state.Apply(command);
            ApplyResult duplicate = state.Apply(command);

            Assert.Multiple(() =>
            {
                Assert.That(duplicate.Accepted, Is.True);
                Assert.That(duplicate.Duplicate, Is.True);
                Assert.That(duplicate.Receipt, Is.EqualTo(first.Receipt));
                Assert.That(state.GetHealth().QueueDepth, Is.EqualTo(1));
            });

            state.PollMappedSnapshot();
            state.Apply(Command("release-2", 2, TomodachiButtonAction.Release));
            state.PollMappedSnapshot();
            state.Apply(Command("press-3", 3, TomodachiButtonAction.Press));
            ApplyResult evictedReplay = state.Apply(command);

            Assert.Multiple(() =>
            {
                Assert.That(state.GetHealth().CommandResultCount, Is.LessThanOrEqualTo(2));
                Assert.That(state.GetHealth().CommandResultCapacity, Is.EqualTo(2));
                Assert.That(evictedReplay.Accepted, Is.False);
                Assert.That(evictedReplay.Duplicate, Is.False);
                Assert.That(evictedReplay.Receipt.Detail, Is.EqualTo("sequence-replay"));
            });
        }

        [Test]
        public void EvictedCommandCannotReplayAfterSameAuthorityRearm()
        {
            using TomodachiInputState state = new(maxPendingTransitions: 1, maxCommandResults: 1);
            TomodachiInputCommand original = Command("press-1", 1, TomodachiButtonAction.Press);
            state.Arm("arm-1", Authority);
            state.Apply(original);
            state.PollMappedSnapshot();
            state.NeutralizeAndLatch("stop-1", NeutralizeReason.OwnerStop);
            state.PollMappedSnapshot();
            state.Arm("arm-2", Authority);

            TomodachiInputCommand expiredEvictor = Command("expired-99", 99, TomodachiButtonAction.Release) with
            {
                ExpiresAt = DateTimeOffset.UnixEpoch,
            };
            Assert.That(state.Apply(expiredEvictor).Receipt.Detail, Is.EqualTo("expired"));

            ApplyResult replay = state.Apply(original);
            PollResult poll = state.PollMappedSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(replay.Accepted, Is.False);
                Assert.That(replay.Duplicate, Is.False);
                Assert.That(replay.Receipt.Detail, Is.EqualTo("sequence-replay"));
                Assert.That(poll.AllNeutral, Is.True);
                Assert.That(state.GetHealth().LastAcceptedSequence, Is.EqualTo(1));
            });
        }

        [Test]
        public void OldOrEqualSequenceWithDifferentCommandIdIsRejectedWithoutMutation()
        {
            using TomodachiInputState state = new();
            state.Arm("arm-1", Authority);
            state.Apply(Command("press-10", 10, TomodachiButtonAction.Press));

            ApplyResult replay = state.Apply(Command("release-10", 10, TomodachiButtonAction.Release));
            PollResult poll = state.PollMappedSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(replay.Accepted, Is.False);
                Assert.That(replay.Receipt.Detail, Is.EqualTo("sequence-replay"));
                Assert.That(poll.Snapshot.IsPressed(GamepadButtonInputId.A), Is.True);
                Assert.That(state.GetHealth().QueueDepth, Is.Zero);
            });
        }

        [Test]
        public void SequenceGapIsAcceptedWithinSameAuthorityEpoch()
        {
            using TomodachiInputState state = new();
            state.Arm("arm-1", Authority);
            state.Apply(Command("press-1", 1, TomodachiButtonAction.Press));

            ApplyResult gap = state.Apply(Command("release-100", 100, TomodachiButtonAction.Release));

            Assert.Multiple(() =>
            {
                Assert.That(gap.Accepted, Is.True);
                Assert.That(state.GetHealth().LastAcceptedSequence, Is.EqualTo(100));
            });
        }

        [Test]
        public void ExpiredCommandIsRejectedNeutral()
        {
            using TomodachiInputState state = new();
            state.Arm("arm-1", Authority);
            TomodachiInputCommand expired = Command("expired", 1, TomodachiButtonAction.Press) with
            {
                ExpiresAt = DateTimeOffset.UnixEpoch,
            };

            ApplyResult result = state.Apply(expired);
            PollResult poll = state.PollMappedSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.Receipt.Detail, Is.EqualTo("expired"));
                Assert.That(poll.AllNeutral, Is.True);
                Assert.That(state.GetHealth().LastAcceptedSequence, Is.EqualTo(-1));
            });
        }

        [Test]
        public void RepeatedSemanticPressDoesNotCreateAnotherRisingEdge()
        {
            using TomodachiInputState state = new();
            state.Arm("arm-1", Authority);

            ApplyResult first = state.Apply(Command("press-1", 1, TomodachiButtonAction.Press));
            ApplyResult repeated = state.Apply(Command("press-2", 2, TomodachiButtonAction.Press));

            Assert.Multiple(() =>
            {
                Assert.That(first.Accepted, Is.True);
                Assert.That(repeated.Accepted, Is.True);
                Assert.That(repeated.Receipt.Detail, Is.EqualTo("coalesced"));
                Assert.That(state.GetHealth().QueueDepth, Is.EqualTo(1));
                Assert.That(state.GetHealth().LastAcceptedSequence, Is.EqualTo(2));
            });
        }

        [Test]
        public void QueueOverflowNeutralizesAndLatches()
        {
            using TomodachiInputState state = new(maxPendingTransitions: 2);
            state.Arm("arm-1", Authority);
            state.Apply(Command("press-1", 1, TomodachiButtonAction.Press));
            state.Apply(Command("release-2", 2, TomodachiButtonAction.Release));

            ApplyResult overflow = state.Apply(Command("press-3", 3, TomodachiButtonAction.Press));
            ProviderHealth health = state.GetHealth();
            PollResult poll = state.PollMappedSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(overflow.Accepted, Is.False);
                Assert.That(overflow.Receipt.Detail, Is.EqualTo("queue-overflow"));
                Assert.That(health.Latched, Is.True);
                Assert.That(health.AllNeutral, Is.True);
                Assert.That(health.QueueDepth, Is.Zero);
                Assert.That(poll.AllNeutral, Is.True);
                Assert.That(state.Apply(Command("release-4", 4, TomodachiButtonAction.Release)).Accepted, Is.False);
            });
        }

        [Test]
        public void MissedBridgeHeartbeatNeutralizesAndLatchesUsingFakeTime()
        {
            ManualTimeProvider clock = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
            using TomodachiInputState state = new(
                timeProvider: clock,
                watchdogTimeout: TimeSpan.FromMilliseconds(500));
            state.Arm("arm-1", Authority);
            state.ObserveBridgeHeartbeat(Authority);
            state.Apply(Command("press-1", 1, TomodachiButtonAction.Press) with
            {
                ExpiresAt = clock.GetUtcNow().AddMinutes(1),
            });
            Assert.That(state.PollMappedSnapshot().Snapshot.IsPressed(GamepadButtonInputId.A), Is.True);

            clock.Advance(TimeSpan.FromMilliseconds(501));
            PollResult stalePoll = state.PollMappedSnapshot();
            ProviderHealth health = state.GetHealth();

            Assert.Multiple(() =>
            {
                Assert.That(stalePoll.AllNeutral, Is.True);
                Assert.That(health.Stale, Is.True);
                Assert.That(health.Latched, Is.True);
                Assert.That(health.AllNeutral, Is.True);
            });
        }

        [Test]
        public void EmergencyStopClearsVisibleAndPendingStateAndRejectsUntilRearm()
        {
            using TomodachiInputState state = new();
            state.Arm("arm-1", Authority);
            state.Apply(Command("press-1", 1, TomodachiButtonAction.Press));
            state.PollMappedSnapshot();
            state.Apply(Command("release-2", 2, TomodachiButtonAction.Release));

            NeutralizeResult stop = state.NeutralizeAndLatch("stop-1", NeutralizeReason.OwnerStop);
            ApplyResult rejected = state.Apply(Command("press-3", 3, TomodachiButtonAction.Press));

            Assert.Multiple(() =>
            {
                Assert.That(stop.Latched, Is.True);
                Assert.That(stop.AllNeutralSampled, Is.False);
                Assert.That(state.GetHealth().AllNeutral, Is.True);
                Assert.That(state.GetHealth().QueueDepth, Is.Zero);
                Assert.That(rejected.Receipt.Detail, Is.EqualTo("latched"));
                Assert.That(state.Arm("arm-1", Authority).Armed, Is.False);
                Assert.That(state.Arm("arm-2", Authority).Armed, Is.True);
                Assert.That(state.Apply(Command("press-4", 3, TomodachiButtonAction.Press)).Accepted, Is.True);
            });
        }

        [Test]
        public void AllNeutralReceiptCompletesOnlyAfterPostStopPoll()
        {
            using TomodachiInputState state = new(maxStopResults: 2);
            state.Arm("arm-1", Authority);
            state.Apply(Command("press-1", 1, TomodachiButtonAction.Press));
            state.PollMappedSnapshot();

            NeutralizeResult immediate = state.NeutralizeAndLatch("stop-1", NeutralizeReason.OwnerStop);
            NeutralizeResult beforePoll = state.NeutralizeAndLatch("stop-1", NeutralizeReason.OwnerStop);
            NeutralizeResult secondStop = state.NeutralizeAndLatch("stop-2", NeutralizeReason.ProtocolFailure);
            PollResult poll = state.PollMappedSnapshot();
            NeutralizeResult afterPoll = state.NeutralizeAndLatch("stop-1", NeutralizeReason.OwnerStop);
            NeutralizeResult secondAfterPoll = state.NeutralizeAndLatch("stop-2", NeutralizeReason.ProtocolFailure);
            state.NeutralizeAndLatch("stop-3", NeutralizeReason.Disconnected);
            ProviderHealth boundedHealth = state.GetHealth();

            Assert.Multiple(() =>
            {
                Assert.That(immediate.AllNeutralSampled, Is.False);
                Assert.That(beforePoll.AllNeutralSampled, Is.False);
                Assert.That(secondStop.AllNeutralSampled, Is.False);
                Assert.That(poll.AllNeutralSampled, Is.True);
                Assert.That(afterPoll.Duplicate, Is.True);
                Assert.That(afterPoll.AllNeutralSampled, Is.True);
                Assert.That(secondAfterPoll.AllNeutralSampled, Is.True);
                Assert.That(afterPoll.NeutralGeneration, Is.LessThan(poll.NeutralGeneration));
                Assert.That(secondAfterPoll.NeutralGeneration, Is.EqualTo(poll.NeutralGeneration));
                Assert.That(boundedHealth.StopResultCount, Is.EqualTo(2));
                Assert.That(boundedHealth.StopResultCapacity, Is.EqualTo(2));
            });
        }

        [Test]
        public void ProviderPollingIsReadyForNonMutatingProbeBeforeArm()
        {
            using TomodachiInputState state = new();

            state.PollMappedSnapshot();
            ProviderHealth health = state.GetHealth();

            Assert.Multiple(() =>
            {
                Assert.That(health.Armed, Is.False);
                Assert.That(health.Polling, Is.True);
                Assert.That(health.Ready, Is.True);
                Assert.That(health.AllNeutral, Is.True);
            });
        }

        [Test]
        public void ProviderHealthDistinguishesReadyPollingStaleLatchedAndAllNeutral()
        {
            ManualTimeProvider clock = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
            using TomodachiInputState state = new(
                timeProvider: clock,
                watchdogTimeout: TimeSpan.FromMilliseconds(500));

            ProviderHealth initial = state.GetHealth();
            state.Arm("arm-1", Authority);
            state.ObserveBridgeHeartbeat(Authority);
            ProviderHealth armed = state.GetHealth();
            state.PollMappedSnapshot();
            ProviderHealth polling = state.GetHealth();

            state.Apply(Command("press-1", 1, TomodachiButtonAction.Press) with
            {
                ExpiresAt = clock.GetUtcNow().AddMinutes(1),
            });
            state.PollMappedSnapshot();
            ProviderHealth active = state.GetHealth();

            clock.Advance(TimeSpan.FromMilliseconds(501));
            ProviderHealth stale = state.GetHealth();
            state.Arm("arm-2", Authority);
            state.ObserveBridgeHeartbeat(Authority);
            state.PollMappedSnapshot();
            ProviderHealth recovered = state.GetHealth();

            Assert.Multiple(() =>
            {
                Assert.That(initial.Ready, Is.False);
                Assert.That(initial.Polling, Is.False);
                Assert.That(initial.AllNeutral, Is.True);
                Assert.That(armed.Armed, Is.True);
                Assert.That(armed.Ready, Is.False);
                Assert.That(polling.Polling, Is.True);
                Assert.That(polling.Ready, Is.True);
                Assert.That(active.AllNeutral, Is.False);
                Assert.That(stale.Stale, Is.True);
                Assert.That(stale.Latched, Is.True);
                Assert.That(stale.Ready, Is.False);
                Assert.That(stale.AllNeutral, Is.True);
                Assert.That(recovered.Ready, Is.True);
                Assert.That(recovered.AllNeutral, Is.True);
            });
        }

        private sealed class EmptyGamepadDriver : IGamepadDriver
        {
            public string DriverName => "empty";
            public ReadOnlySpan<string> GamepadsIds => [];
            public int DisposeCount { get; private set; }

            public event Action<string> OnGamepadConnected
            {
                add { }
                remove { }
            }

            public event Action<string> OnGamepadDisconnected
            {
                add { }
                remove { }
            }

            public IGamepad GetGamepad(string id) => null;
            public IEnumerable<IGamepad> GetGamepads() => [];
            public void Dispose() => DisposeCount++;
        }

        private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
        {
            private DateTimeOffset _utcNow = utcNow;

            public override DateTimeOffset GetUtcNow() => _utcNow;

            public void Advance(TimeSpan duration)
            {
                _utcNow += duration;
            }
        }
    }
}
