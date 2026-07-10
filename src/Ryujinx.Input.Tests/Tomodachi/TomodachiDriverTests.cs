using NUnit.Framework;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.HLE.HOS.Services.Hid;
using Ryujinx.Input.Drivers;
using Ryujinx.Input.Tomodachi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ryujinx.Input.Tests.Tomodachi
{
    [TestFixture]
    public class TomodachiDriverTests
    {
        private static readonly TomodachiAuthorityEpoch Authority = new("server", "room", "lease", "session");

        [Test]
        public void ClearNeutralizesVirtualAndDelegatesToPrimaryDriver()
        {
            TrackingDriver primary = new("sdl-1");
            using TomodachiInputState state = new();
            state.Arm("arm-1", Authority);
            state.Apply(new TomodachiInputCommand(
                "press-1",
                Authority,
                1,
                DateTimeOffset.MaxValue,
                GamepadButtonInputId.A,
                TomodachiButtonAction.Press,
                "trace-1"));
            state.PollMappedSnapshot();
            using TomodachiGamepadDriver virtualDriver = new(state);
            using CompositeGamepadDriver composite = new(primary, virtualDriver);

            composite.Clear();

            Assert.Multiple(() =>
            {
                Assert.That(primary.ClearCount, Is.EqualTo(1));
                Assert.That(state.GetHealth().AllNeutral, Is.True);
                Assert.That(state.GetHealth().Armed, Is.True);
                Assert.That(state.GetHealth().Latched, Is.False);
                Assert.That(state.PollMappedSnapshot().AllNeutral, Is.True);
            });
        }

        [Test]
        public void CompositePreservesPrimaryIdsAndAddsExactlyOneVirtualId()
        {
            TrackingDriver primary = new("sdl-1", "sdl-2");
            using TomodachiInputState state = new();
            using CompositeGamepadDriver composite = new(primary, new TomodachiGamepadDriver(state));

            string[] ids = composite.GamepadsIds.ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(ids, Is.EqualTo(new[] { "sdl-1", "sdl-2", TomodachiGamepadDriver.VirtualGamepadId }));
                Assert.That(ids.Count(id => id == TomodachiGamepadDriver.VirtualGamepadId), Is.EqualTo(1));
            });
        }

        [Test]
        public void CompositeRoutesPrimaryAndVirtualIdsAndRejectsCollisions()
        {
            TrackingDriver primary = new("sdl-1");
            using TomodachiInputState state = new();
            using CompositeGamepadDriver composite = new(primary, new TomodachiGamepadDriver(state));

            using IGamepad physical = composite.GetGamepad("sdl-1");
            using IGamepad virtualGamepad = composite.GetGamepad(TomodachiGamepadDriver.VirtualGamepadId);

            Assert.Multiple(() =>
            {
                Assert.That(physical.Id, Is.EqualTo("sdl-1"));
                Assert.That(virtualGamepad.Id, Is.EqualTo(TomodachiGamepadDriver.VirtualGamepadId));
                Assert.That(composite.GetGamepad("missing"), Is.Null);
                Assert.That(
                    () => new CompositeGamepadDriver(
                        new TrackingDriver(TomodachiGamepadDriver.VirtualGamepadId),
                        new TomodachiGamepadDriver(new TomodachiInputState())),
                    Throws.TypeOf<InvalidOperationException>());
            });
        }

        [Test]
        public void TomodachiGamepadMappedSnapshotUsesCanonicalAAndNeutralSticks()
        {
            using TomodachiInputState state = new();
            state.Arm("arm-1", Authority);
            state.Apply(new TomodachiInputCommand(
                "press-1",
                Authority,
                1,
                DateTimeOffset.MaxValue,
                GamepadButtonInputId.A,
                TomodachiButtonAction.Press,
                "trace-1"));
            using TomodachiGamepad gamepad = new(state);
            gamepad.SetConfiguration(new StandardControllerInputConfig());

            GamepadStateSnapshot snapshot = gamepad.GetMappedStateSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.IsPressed(GamepadButtonInputId.A), Is.True);
                Assert.That(snapshot.IsPressed(GamepadButtonInputId.B), Is.False);
                Assert.That(snapshot.GetStick(StickInputId.Left), Is.EqualTo((0f, 0f)));
                Assert.That(snapshot.GetStick(StickInputId.Right), Is.EqualTo((0f, 0f)));
                Assert.That(gamepad.Features, Is.EqualTo(GamepadFeaturesFlag.None));
            });
        }

        [Test]
        public void DisposeNeutralizesAndIsIdempotent()
        {
            using TomodachiInputState state = new();
            state.Arm("arm-1", Authority);
            state.Apply(new TomodachiInputCommand(
                "press-1",
                Authority,
                1,
                DateTimeOffset.MaxValue,
                GamepadButtonInputId.A,
                TomodachiButtonAction.Press,
                "trace-1"));
            TomodachiGamepad gamepad = new(state);
            gamepad.GetMappedStateSnapshot();
            state.Apply(new TomodachiInputCommand(
                "release-2",
                Authority,
                2,
                DateTimeOffset.MaxValue,
                GamepadButtonInputId.A,
                TomodachiButtonAction.Release,
                "trace-2"));

            gamepad.Dispose();
            gamepad.Dispose();

            TrackingDriver primary = new("sdl-1");
            TomodachiInputState driverState = new();
            CompositeGamepadDriver composite = new(primary, new TomodachiGamepadDriver(driverState));
            composite.Dispose();
            composite.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(gamepad.IsConnected, Is.False);
                Assert.That(state.GetHealth().AllNeutral, Is.True);
                Assert.That(state.GetHealth().QueueDepth, Is.Zero);
                Assert.That(state.GetHealth().Latched, Is.False);
                Assert.That(primary.DisposeCount, Is.EqualTo(1));
                Assert.That(driverState.GetHealth().Disposed, Is.True);
                Assert.That(driverState.GetHealth().AllNeutral, Is.True);
            });
            driverState.Dispose();
        }

        private sealed class TrackingDriver(params string[] ids) : IGamepadDriver
        {
            private readonly string[] _ids = ids;

            public string DriverName => "tracking";
            public ReadOnlySpan<string> GamepadsIds => _ids;
            public int ClearCount { get; private set; }
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

            public void Clear() => ClearCount++;
            public void Dispose() => DisposeCount++;
            public IGamepad GetGamepad(string id) => Array.IndexOf(_ids, id) >= 0 ? new StubGamepad(id) : null;

            public IEnumerable<IGamepad> GetGamepads()
            {
                foreach (string id in _ids)
                {
                    yield return new StubGamepad(id);
                }
            }
        }

        private sealed class StubGamepad(string id) : IGamepad
        {
            public GamepadFeaturesFlag Features => GamepadFeaturesFlag.None;
            public string Id { get; } = id;
            public string Name => Id;
            public bool IsConnected => true;
            public void Dispose() { }
            public GamepadStateSnapshot GetMappedStateSnapshot() => default;
            public Vector3 GetMotionData(MotionInputId inputId) => Vector3.Zero;
            public GamepadStateSnapshot GetStateSnapshot() => default;
            public (float, float) GetStick(StickInputId inputId) => (0f, 0f);
            public bool HDRumble(VibrationValue left, VibrationValue right) => false;
            public bool IsPressed(GamepadButtonInputId inputId) => false;
            public bool Rumble(float lowFrequency, float highFrequency, uint durationMs) => false;
            public void SetConfiguration(InputConfig configuration) { }
            public void SetLed(uint packedRgb) { }
            public void SetTriggerThreshold(float triggerThreshold) { }
        }
    }
}
