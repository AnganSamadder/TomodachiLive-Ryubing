using System;
using System.Collections.Generic;

namespace Ryujinx.Input.Tomodachi
{
    public sealed class TomodachiGamepadDriver : IGamepadDriver
    {
        public const string VirtualGamepadId = "tomodachi-live:virtual:0";

        private static readonly string[] VirtualIds = [VirtualGamepadId];
        private readonly TomodachiInputState _state;
        private bool _disposed;

        public TomodachiGamepadDriver(TomodachiInputState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public string DriverName => "Tomodachi Live Virtual Gamepad";
        public ReadOnlySpan<string> GamepadsIds => _disposed ? [] : VirtualIds;

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

        public IGamepad GetGamepad(string id)
        {
            return !_disposed && string.Equals(id, VirtualGamepadId, StringComparison.Ordinal)
                ? new TomodachiGamepad(_state)
                : null;
        }

        public IEnumerable<IGamepad> GetGamepads()
        {
            IGamepad gamepad = GetGamepad(VirtualGamepadId);
            return gamepad == null ? [] : [gamepad];
        }

        public void Clear()
        {
            if (!_disposed)
            {
                _state.Clear();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _state.Dispose();
        }
    }
}
