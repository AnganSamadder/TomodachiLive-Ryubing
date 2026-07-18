using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace Ryujinx.Input.Tomodachi.Ipc
{
    public sealed class TomodachiStatusProofAuthority : ITomodachiStatusProofSource, IDisposable
    {
        private static readonly TimeSpan ObservationInterval = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan ObservationEligibilityDelay = TimeSpan.FromMilliseconds(250);
        private const int MaximumObservationCount = 64;
        private static readonly JsonSerializerOptions IdentityJsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly object _gate = new();
        private readonly string _localSavePath;
        private readonly string _identityDigest;
        private readonly TimeProvider _timeProvider;
        private readonly List<TomodachiStatusProofSnapshot> _observations = new();
        private Func<string> _sampleState;
        private ITimer _observationTimer;
        private long _providerEpoch;
        private bool _exited;
        private bool _disposed;

        public TomodachiStatusProofAuthority(
            string identitySavePath,
            string localSavePath,
            string activeSessionPath,
            TimeProvider timeProvider = null)
        {
            identitySavePath = RequiredTrimmed(identitySavePath, nameof(identitySavePath));
            _localSavePath = NormalizeLocalPath(localSavePath);
            string normalizedActiveSessionPath = string.IsNullOrWhiteSpace(activeSessionPath)
                ? null
                : activeSessionPath.Trim();
            _identityDigest = CreateIdentityDigest(identitySavePath, normalizedActiveSessionPath);
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public bool TryBind(string emulatorSavePath, Func<string> sampleState)
        {
            ArgumentNullException.ThrowIfNull(sampleState);
            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                InvalidateBindingCore();
                if (_providerEpoch == long.MaxValue)
                {
                    return false;
                }

                string normalizedEmulatorPath;
                try
                {
                    normalizedEmulatorPath = NormalizeLocalPath(emulatorSavePath);
                }
                catch (ArgumentException)
                {
                    return false;
                }

                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!Directory.Exists(_localSavePath) ||
                    !Directory.Exists(normalizedEmulatorPath) ||
                    !string.Equals(_localSavePath, normalizedEmulatorPath, comparison))
                {
                    return false;
                }

                _sampleState = sampleState;
                _providerEpoch++;
                ObserveCore();
                _observationTimer = _timeProvider.CreateTimer(
                    _ => Observe(),
                    null,
                    ObservationInterval,
                    ObservationInterval);
                return true;
            }
        }

        public void InvalidateBinding()
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    InvalidateBindingCore();
                }
            }
        }

        public void MarkExited()
        {
            lock (_gate)
            {
                if (_disposed || _sampleState is null || _exited || _providerEpoch == long.MaxValue)
                {
                    return;
                }

                _exited = true;
                _providerEpoch++;
                ObserveCore();
            }
        }

        public bool TrySample(out TomodachiStatusProofSnapshot snapshot)
        {
            lock (_gate)
            {
                snapshot = default;
                if (_disposed || _sampleState is null || _providerEpoch <= 0)
                {
                    return false;
                }

                DateTimeOffset cutoff = _timeProvider.GetUtcNow() - ObservationEligibilityDelay;
                for (int index = _observations.Count - 1; index >= 0; index--)
                {
                    if (_observations[index].ObservedAt <= cutoff)
                    {
                        snapshot = _observations[index];
                        return true;
                    }
                }

                return false;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                InvalidateBindingCore();
            }
        }

        private void Observe()
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    ObserveCore();
                }
            }
        }

        private void ObserveCore()
        {
            if (_sampleState is null)
            {
                return;
            }

            string state;
            try
            {
                state = _exited ? "exited" : _sampleState();
            }
            catch
            {
                return;
            }

            if (state is not ("paused" or "exited" or "running" or "unknown"))
            {
                return;
            }

            if (_observations.Count == MaximumObservationCount)
            {
                _observations.RemoveAt(0);
            }

            _observations.Add(new TomodachiStatusProofSnapshot(
                state,
                _identityDigest,
                _providerEpoch,
                _timeProvider.GetUtcNow()));
        }

        private void InvalidateBindingCore()
        {
            _observationTimer?.Dispose();
            _observationTimer = null;
            _sampleState = null;
            _exited = false;
            _observations.Clear();
        }

        private static string CreateIdentityDigest(string savePath, string activeSessionPath)
        {
            string canonicalIdentity = JsonSerializer.Serialize(
                new string[] { "tomodachi-game-save-identity-1", savePath, activeSessionPath },
                IdentityJsonOptions);
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity));
            return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
        }

        private static string NormalizeLocalPath(string value)
        {
            value = RequiredTrimmed(value, nameof(value));
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }

        private static string RequiredTrimmed(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("status-proof-identity-required", parameterName);
            }

            return value.Trim();
        }
    }
}
