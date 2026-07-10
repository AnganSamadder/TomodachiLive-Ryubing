using System;
using System.Collections.Generic;

namespace Ryujinx.Input.Drivers
{
    public sealed class CompositeGamepadDriver : IGamepadDriver
    {
        private readonly IGamepadDriver _primary;
        private readonly IGamepadDriver _secondary;
        private bool _disposed;

        public CompositeGamepadDriver(IGamepadDriver primary, IGamepadDriver secondary)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _secondary = secondary ?? throw new ArgumentNullException(nameof(secondary));
            EnsureNoCollisions();
            _primary.OnGamepadConnected += HandleConnected;
            _primary.OnGamepadDisconnected += HandleDisconnected;
            _secondary.OnGamepadConnected += HandleConnected;
            _secondary.OnGamepadDisconnected += HandleDisconnected;
        }

        public string DriverName => $"{_primary.DriverName} + {_secondary.DriverName}";

        public ReadOnlySpan<string> GamepadsIds
        {
            get
            {
                EnsureNoCollisions();
                string[] primaryIds = _primary.GamepadsIds.ToArray();
                string[] secondaryIds = _secondary.GamepadsIds.ToArray();
                string[] ids = new string[primaryIds.Length + secondaryIds.Length];
                primaryIds.CopyTo(ids, 0);
                secondaryIds.CopyTo(ids, primaryIds.Length);
                return ids;
            }
        }

        public event Action<string> OnGamepadConnected;
        public event Action<string> OnGamepadDisconnected;

        public IGamepad GetGamepad(string id)
        {
            bool primaryOwns = Contains(_primary.GamepadsIds, id);
            bool secondaryOwns = Contains(_secondary.GamepadsIds, id);
            if (primaryOwns && secondaryOwns)
            {
                throw new InvalidOperationException($"Gamepad id collision: {id}");
            }

            return primaryOwns ? _primary.GetGamepad(id) : secondaryOwns ? _secondary.GetGamepad(id) : null;
        }

        public IEnumerable<IGamepad> GetGamepads()
        {
            List<IGamepad> gamepads = [];
            string[] ids = GamepadsIds.ToArray();
            foreach (string id in ids)
            {
                IGamepad gamepad = GetGamepad(id);
                if (gamepad != null)
                {
                    gamepads.Add(gamepad);
                }
            }

            return gamepads;
        }

        public void Clear()
        {
            _primary.Clear();
            _secondary.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _primary.OnGamepadConnected -= HandleConnected;
            _primary.OnGamepadDisconnected -= HandleDisconnected;
            _secondary.OnGamepadConnected -= HandleConnected;
            _secondary.OnGamepadDisconnected -= HandleDisconnected;
            _secondary.Dispose();
            _primary.Dispose();
        }

        private static bool Contains(ReadOnlySpan<string> ids, string id)
        {
            foreach (string candidate in ids)
            {
                if (string.Equals(candidate, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureNoCollisions()
        {
            foreach (string id in _primary.GamepadsIds)
            {
                if (Contains(_secondary.GamepadsIds, id))
                {
                    throw new InvalidOperationException($"Gamepad id collision: {id}");
                }
            }
        }

        private void HandleConnected(string id) => OnGamepadConnected?.Invoke(id);
        private void HandleDisconnected(string id) => OnGamepadDisconnected?.Invoke(id);
    }
}
