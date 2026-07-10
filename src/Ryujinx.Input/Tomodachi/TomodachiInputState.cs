using System;
using System.Collections.Generic;

namespace Ryujinx.Input.Tomodachi
{
    public enum ApplyDisposition
    {
        Accepted,
        Duplicate,
        Rejected,
        SupersededByNeutralize,
    }

    public enum NeutralizeReason
    {
        OwnerStop,
        WatchdogExpired,
        Disconnected,
        QueueOverflow,
        CommandLedgerOverflow,
        ProtocolFailure,
        ProviderDisposal,
        ProviderDisabled,
        Rearmed,
    }

    public readonly record struct CommandReceipt(
        string CommandId,
        long Sequence,
        ApplyDisposition Disposition,
        bool Sampled,
        long? ProviderPoll,
        DateTimeOffset? SampledAt,
        string Detail);

    public readonly record struct ArmResult(bool Armed, string Detail);

    public readonly record struct NeutralizeResult(
        bool Latched,
        bool Duplicate,
        long NeutralGeneration,
        bool AllNeutralSampled,
        string Detail);

    public readonly record struct ApplyResult(
        bool Accepted,
        bool Duplicate,
        CommandReceipt Receipt);

    public readonly record struct ProviderHealth(
        bool Enabled,
        bool Armed,
        bool Ready,
        bool Polling,
        bool Stale,
        bool Latched,
        bool Disposed,
        bool AllNeutral,
        int QueueDepth,
        int QueueCapacity,
        int CommandResultCount,
        int CommandResultCapacity,
        long LastAcceptedSequence,
        long ProviderPoll,
        long NeutralGeneration);

    public readonly record struct PollResult(
        GamepadStateSnapshot Snapshot,
        long ProviderPoll,
        bool AllNeutral,
        long NeutralGeneration,
        DateTimeOffset SampledAt,
        bool AllNeutralSampled);

    public sealed class TomodachiInputState : ITomodachiInputControl, ITomodachiInputPollSource, IDisposable
    {
        private readonly object _lock = new();
        private readonly Queue<(bool Pressed, string CommandId)> _transitions = new();
        private readonly Dictionary<string, CommandReceipt> _commandReceipts = new(StringComparer.Ordinal);
        private readonly Queue<string> _commandReceiptOrder = new();
        private readonly Dictionary<string, bool> _pendingCommandStates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NeutralizeResult> _stopResults = new(StringComparer.Ordinal);
        private readonly TimeProvider _timeProvider;
        private readonly int _maxPendingTransitions;
        private readonly int _maxCommandResults;
        private readonly TimeSpan _watchdogTimeout;

        private TomodachiAuthorityEpoch _authority;
        private string _armId;
        private bool _armed;
        private bool _visibleA;
        private bool _desiredA;
        private bool _latched;
        private bool _stale;
        private bool _disposed;
        private DateTimeOffset _lastHeartbeatAt;
        private DateTimeOffset? _lastPollAt;
        private long _lastAcceptedSequence = -1;
        private long _providerPoll;
        private long _neutralGeneration;
        private long _lastNeutralSampledGeneration;

        public TomodachiInputState(
            TimeProvider timeProvider = null,
            int maxPendingTransitions = 64,
            int maxCommandResults = 1024,
            TimeSpan? watchdogTimeout = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxPendingTransitions, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxCommandResults, maxPendingTransitions);
            _timeProvider = timeProvider ?? TimeProvider.System;
            _maxPendingTransitions = maxPendingTransitions;
            _maxCommandResults = maxCommandResults;
            _watchdogTimeout = watchdogTimeout ?? TimeSpan.FromSeconds(2);
            if (_watchdogTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(watchdogTimeout));
            }
        }

        public ArmResult Arm(string armId, TomodachiAuthorityEpoch authority)
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return new ArmResult(false, "disposed");
                }

                if (string.IsNullOrWhiteSpace(armId))
                {
                    return new ArmResult(false, "invalid-arm-id");
                }

                if (!IsValidAuthority(authority))
                {
                    return new ArmResult(false, "invalid-authority");
                }

                if (string.Equals(_armId, armId, StringComparison.Ordinal))
                {
                    if (_latched)
                    {
                        return new ArmResult(false, "new-arm-required");
                    }

                    if (authority != _authority)
                    {
                        return new ArmResult(false, "arm-id-conflict");
                    }

                    if (_armed)
                    {
                        return new ArmResult(true, "already-armed");
                    }
                }

                if (_armed && !_latched)
                {
                    NeutralizeAndLatchCore(NeutralizeReason.Rearmed);
                }

                _authority = authority;
                _armId = armId;
                _armed = true;
                _latched = false;
                _stale = false;
                _visibleA = false;
                _desiredA = false;
                _transitions.Clear();
                _lastAcceptedSequence = -1;
                _lastHeartbeatAt = _timeProvider.GetUtcNow();
                _lastPollAt = null;
                return new ArmResult(true, "armed");
            }
        }

        public void ObserveBridgeHeartbeat(TomodachiAuthorityEpoch authority)
        {
            lock (_lock)
            {
                if (_armed && !_latched && !_disposed && authority == _authority)
                {
                    _lastHeartbeatAt = _timeProvider.GetUtcNow();
                }
            }
        }

        public NeutralizeResult NeutralizeAndLatch(string stopId, NeutralizeReason reason)
        {
            lock (_lock)
            {
                if (_stopResults.TryGetValue(stopId, out NeutralizeResult previous))
                {
                    return previous with { Duplicate = true };
                }

                NeutralizeAndLatchCore(reason);
                NeutralizeResult result = new(
                    Latched: true,
                    Duplicate: false,
                    NeutralGeneration: _neutralGeneration,
                    AllNeutralSampled: false,
                    Detail: reason.ToString());
                _stopResults.Add(stopId, result);
                return result;
            }
        }

        public ApplyResult Apply(in TomodachiInputCommand command)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(command.CommandId))
                {
                    return RejectedWithoutCaching(command, "invalid-command-id");
                }

                if (string.IsNullOrWhiteSpace(command.TraceId))
                {
                    return RejectedWithoutCaching(command, "invalid-trace-id");
                }

                if (_commandReceipts.TryGetValue(command.CommandId, out CommandReceipt previousReceipt))
                {
                    return new ApplyResult(
                        Accepted: previousReceipt.Disposition is ApplyDisposition.Accepted or ApplyDisposition.Duplicate,
                        Duplicate: true,
                        Receipt: previousReceipt);
                }

                CheckWatchdog();

                if (!EnsureCommandReceiptSlot())
                {
                    return Rejected(command, "command-ledger-overflow");
                }

                if (_latched)
                {
                    return Rejected(command, "latched");
                }

                if (!_armed || _disposed)
                {
                    return Rejected(command, "not-armed");
                }

                if (command.Authority != _authority)
                {
                    return Rejected(command, "authority-mismatch");
                }

                if (command.ExpiresAt <= _timeProvider.GetUtcNow())
                {
                    return Rejected(command, "expired");
                }

                if (command.Button != GamepadButtonInputId.A)
                {
                    return Rejected(command, "unsupported-input");
                }

                if (!Enum.IsDefined(command.Action))
                {
                    return Rejected(command, "unsupported-action");
                }

                if (command.Sequence <= _lastAcceptedSequence)
                {
                    return Rejected(command, "sequence-replay");
                }

                bool desiredA = command.Action == TomodachiButtonAction.Press;
                _lastAcceptedSequence = command.Sequence;

                string detail;
                if (desiredA == _desiredA)
                {
                    detail = "coalesced";
                }
                else
                {
                    if (_transitions.Count >= _maxPendingTransitions)
                    {
                        NeutralizeAndLatchCore(NeutralizeReason.QueueOverflow);
                        return Rejected(command, "queue-overflow");
                    }

                    _desiredA = desiredA;
                    _transitions.Enqueue((desiredA, command.CommandId));
                    detail = "queued";
                }

                CommandReceipt receipt = new(
                    command.CommandId,
                    command.Sequence,
                    ApplyDisposition.Accepted,
                    Sampled: false,
                    ProviderPoll: null,
                    SampledAt: null,
                    Detail: detail);
                _pendingCommandStates.Add(command.CommandId, desiredA);
                StoreReceipt(receipt);

                return new ApplyResult(
                    Accepted: true,
                    Duplicate: false,
                    Receipt: receipt);
            }
        }

        public ProviderHealth GetHealth()
        {
            lock (_lock)
            {
                CheckWatchdog();
                DateTimeOffset now = _timeProvider.GetUtcNow();
                bool polling = _lastPollAt.HasValue && now - _lastPollAt.Value <= _watchdogTimeout;
                bool ready = _armed && polling && !_stale && !_latched && !_disposed;
                return new ProviderHealth(
                    Enabled: true,
                    Armed: _armed,
                    Ready: ready,
                    Polling: polling,
                    Stale: _stale,
                    Latched: _latched,
                    Disposed: _disposed,
                    AllNeutral: !_visibleA && _transitions.Count == 0,
                    QueueDepth: _transitions.Count,
                    QueueCapacity: _maxPendingTransitions,
                    CommandResultCount: _commandReceipts.Count,
                    CommandResultCapacity: _maxCommandResults,
                    LastAcceptedSequence: _lastAcceptedSequence,
                    ProviderPoll: _providerPoll,
                    NeutralGeneration: _neutralGeneration);
            }
        }

        public PollResult PollMappedSnapshot()
        {
            lock (_lock)
            {
                CheckWatchdog();
                _providerPoll++;
                DateTimeOffset sampledAt = _timeProvider.GetUtcNow();
                _lastPollAt = sampledAt;

                if (_transitions.TryDequeue(out (bool Pressed, string CommandId) transition))
                {
                    _visibleA = transition.Pressed;
                }

                CompleteSampledCommandReceipts(_visibleA, sampledAt);

                bool allNeutral = !_visibleA;
                bool allNeutralSampled = allNeutral &&
                    _neutralGeneration > 0 &&
                    _neutralGeneration > _lastNeutralSampledGeneration;
                if (allNeutralSampled)
                {
                    _lastNeutralSampledGeneration = _neutralGeneration;
                    CompleteNeutralReceipts(_neutralGeneration);
                }

                GamepadStateSnapshot snapshot = default;
                snapshot.SetPressed(GamepadButtonInputId.A, _visibleA);

                return new PollResult(
                    Snapshot: snapshot,
                    ProviderPoll: _providerPoll,
                    AllNeutral: allNeutral,
                    NeutralGeneration: _neutralGeneration,
                    SampledAt: sampledAt,
                    AllNeutralSampled: allNeutralSampled);
            }
        }

        public GamepadStateSnapshot GetVisibleSnapshot()
        {
            lock (_lock)
            {
                CheckWatchdog();
                GamepadStateSnapshot snapshot = default;
                snapshot.SetPressed(GamepadButtonInputId.A, _visibleA);
                return snapshot;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _visibleA = false;
                _desiredA = false;
                _transitions.Clear();
                _neutralGeneration++;
                SupersedePendingCommands(NeutralizeReason.ProviderDisabled);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                NeutralizeAndLatchCore(NeutralizeReason.ProviderDisposal);
                _disposed = true;
            }
        }

        private void CompleteSampledCommandReceipts(bool visibleA, DateTimeOffset sampledAt)
        {
            List<string> sampledCommandIds = [];
            foreach (KeyValuePair<string, bool> entry in _pendingCommandStates)
            {
                if (entry.Value == visibleA)
                {
                    sampledCommandIds.Add(entry.Key);
                }
            }

            foreach (string commandId in sampledCommandIds)
            {
                if (_commandReceipts.TryGetValue(commandId, out CommandReceipt receipt))
                {
                    _commandReceipts[commandId] = receipt with
                    {
                        Sampled = true,
                        ProviderPoll = _providerPoll,
                        SampledAt = sampledAt,
                        Detail = "sampled",
                    };
                }

                _pendingCommandStates.Remove(commandId);
            }

            TrimCommandReceipts(_maxCommandResults);
        }

        private bool EnsureCommandReceiptSlot()
        {
            TrimCommandReceipts(_maxCommandResults - 1);
            if (_commandReceipts.Count < _maxCommandResults)
            {
                return true;
            }

            NeutralizeAndLatchCore(NeutralizeReason.CommandLedgerOverflow);
            TrimCommandReceipts(_maxCommandResults - 1);
            return false;
        }

        private void StoreReceipt(CommandReceipt receipt)
        {
            _commandReceipts.Add(receipt.CommandId, receipt);
            _commandReceiptOrder.Enqueue(receipt.CommandId);
            TrimCommandReceipts(_maxCommandResults);
        }

        private void TrimCommandReceipts(int targetCount)
        {
            int remainingToInspect = _commandReceiptOrder.Count;
            while (_commandReceipts.Count > targetCount &&
                   remainingToInspect-- > 0 &&
                   _commandReceiptOrder.TryDequeue(out string commandId))
            {
                if (!_commandReceipts.TryGetValue(commandId, out CommandReceipt receipt))
                {
                    continue;
                }

                bool finalized = receipt.Sampled ||
                    receipt.Disposition is ApplyDisposition.Rejected or ApplyDisposition.SupersededByNeutralize;
                if (finalized)
                {
                    _commandReceipts.Remove(commandId);
                    _pendingCommandStates.Remove(commandId);
                }
                else
                {
                    _commandReceiptOrder.Enqueue(commandId);
                }
            }
        }

        private void CompleteNeutralReceipts(long neutralGeneration)
        {
            List<string> stopIds = [];
            foreach (KeyValuePair<string, NeutralizeResult> entry in _stopResults)
            {
                if (entry.Value.NeutralGeneration == neutralGeneration)
                {
                    stopIds.Add(entry.Key);
                }
            }

            foreach (string stopId in stopIds)
            {
                _stopResults[stopId] = _stopResults[stopId] with { AllNeutralSampled = true };
            }
        }

        private static bool IsValidAuthority(TomodachiAuthorityEpoch authority)
        {
            return !string.IsNullOrWhiteSpace(authority.ServerInstanceId) &&
                !string.IsNullOrWhiteSpace(authority.RoomId) &&
                !string.IsNullOrWhiteSpace(authority.ControlLeaseId) &&
                !string.IsNullOrWhiteSpace(authority.SessionId);
        }

        private void CheckWatchdog()
        {
            if (_armed && !_latched && _timeProvider.GetUtcNow() - _lastHeartbeatAt > _watchdogTimeout)
            {
                _stale = true;
                NeutralizeAndLatchCore(NeutralizeReason.WatchdogExpired);
            }
        }

        private void NeutralizeAndLatchCore(NeutralizeReason reason)
        {
            _visibleA = false;
            _desiredA = false;
            _transitions.Clear();
            _armed = false;
            _latched = true;
            _neutralGeneration++;
            SupersedePendingCommands(reason);
        }

        private void SupersedePendingCommands(NeutralizeReason reason)
        {
            List<string> pendingCommandIds = [];
            foreach (KeyValuePair<string, CommandReceipt> entry in _commandReceipts)
            {
                if (!entry.Value.Sampled && entry.Value.Disposition == ApplyDisposition.Accepted)
                {
                    pendingCommandIds.Add(entry.Key);
                }
            }

            foreach (string commandId in pendingCommandIds)
            {
                CommandReceipt receipt = _commandReceipts[commandId];
                _commandReceipts[commandId] = receipt with
                {
                    Disposition = ApplyDisposition.SupersededByNeutralize,
                    Detail = $"superseded-by-{reason.ToString().ToLowerInvariant()}",
                };
                _pendingCommandStates.Remove(commandId);
            }
        }

        private static ApplyResult RejectedWithoutCaching(in TomodachiInputCommand command, string detail)
        {
            CommandReceipt receipt = new(
                command.CommandId,
                command.Sequence,
                ApplyDisposition.Rejected,
                Sampled: false,
                ProviderPoll: null,
                SampledAt: null,
                Detail: detail);
            return new ApplyResult(false, false, receipt);
        }

        private ApplyResult Rejected(in TomodachiInputCommand command, string detail)
        {
            CommandReceipt receipt = new(
                command.CommandId,
                command.Sequence,
                ApplyDisposition.Rejected,
                Sampled: false,
                ProviderPoll: null,
                SampledAt: null,
                Detail: detail);
            StoreReceipt(receipt);

            return new ApplyResult(
                Accepted: false,
                Duplicate: false,
                Receipt: receipt);
        }
    }
}
