using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.HLE.HOS.Services.Hid;
using System;
using System.Numerics;

namespace Ryujinx.Input.Tomodachi
{
    public sealed class TomodachiGamepad : IGamepad
    {
        private readonly TomodachiInputState _state;
        private bool _disposed;

        public TomodachiGamepad(TomodachiInputState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public GamepadFeaturesFlag Features => GamepadFeaturesFlag.None;
        public string Id => TomodachiGamepadDriver.VirtualGamepadId;
        public string Name => "Tomodachi Live Virtual Gamepad";
        public bool IsConnected => !_disposed;

        public bool IsPressed(GamepadButtonInputId inputId) =>
            !_disposed && inputId == GamepadButtonInputId.A && _state.GetVisibleSnapshot().IsPressed(GamepadButtonInputId.A);

        public (float, float) GetStick(StickInputId inputId) => (0f, 0f);
        public Vector3 GetMotionData(MotionInputId inputId) => Vector3.Zero;
        public void SetTriggerThreshold(float triggerThreshold) { }

        public void SetConfiguration(InputConfig configuration)
        {
            if (configuration is not null and not StandardControllerInputConfig)
            {
                throw new ArgumentException("Tomodachi virtual gamepad requires a standard controller configuration.", nameof(configuration));
            }
        }

        public void SetLed(uint packedRgb) { }
        public bool HDRumble(VibrationValue left, VibrationValue right) => false;
        public bool Rumble(float lowFrequency, float highFrequency, uint durationMs) => false;
        public GamepadStateSnapshot GetMappedStateSnapshot() => _disposed ? default : _state.PollMappedSnapshot().Snapshot;
        public GamepadStateSnapshot GetStateSnapshot() => _disposed ? default : _state.GetVisibleSnapshot();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _state.Clear();
        }
    }
}
