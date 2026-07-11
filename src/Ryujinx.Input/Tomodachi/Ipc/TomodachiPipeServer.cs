using Ryujinx.Input.Drivers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Input.Tomodachi.Ipc
{
    public sealed class TomodachiPipeServer : IAsyncDisposable, IDisposable
    {
        private readonly TomodachiPipeOptions _options;
        private readonly ITomodachiInputControl _control;
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly string _providerInstanceId = $"ryubing-{Guid.NewGuid():N}";
        private NamedPipeServerStream _initialPipe;
        private NamedPipeServerStream _activePipe;
        private Task _runTask;
        private int _disposed;

        private TomodachiPipeServer(TomodachiPipeOptions options, ITomodachiInputControl control)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _control = control ?? throw new ArgumentNullException(nameof(control));
            _initialPipe = CreatePipe(options.PipeName);
        }

        public static TomodachiPipeServer Start(TomodachiPipeOptions options, ITomodachiInputControl control)
        {
            TomodachiPipeServer server = new(options, control);
            server._runTask = server.RunAsync();
            return server;
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _lifetimeCancellation.Cancel();
            _initialPipe?.Dispose();
            Interlocked.Exchange(ref _activePipe, null)?.Dispose();
            if (_runTask is not null)
            {
                try
                {
                    await _runTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            _lifetimeCancellation.Dispose();
            _sendLock.Dispose();
        }

        private static NamedPipeServerStream CreatePipe(string pipeName)
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }

        private async Task RunAsync()
        {
            NamedPipeServerStream pipe = Interlocked.Exchange(ref _initialPipe, null);
            while (!_lifetimeCancellation.IsCancellationRequested)
            {
                pipe ??= CreatePipe(_options.PipeName);
                Volatile.Write(ref _activePipe, pipe);
                try
                {
                    await pipe.WaitForConnectionAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
                    await HandleConnectionAsync(pipe, _lifetimeCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
                {
                    pipe.Dispose();
                    break;
                }
                catch (IOException)
                {
                    pipe.Dispose();
                }
                catch (ObjectDisposedException) when (_lifetimeCancellation.IsCancellationRequested)
                {
                    break;
                }
                finally
                {
                    Interlocked.CompareExchange(ref _activePipe, null, pipe);
                    pipe.Dispose();
                    pipe = null;
                }
            }
        }

        private async Task HandleConnectionAsync(Stream pipe, CancellationToken lifetimeToken)
        {
            bool neutralized = false;
            NeutralizeReason disconnectReason = NeutralizeReason.Disconnected;
            try
            {
                using JsonDocument hello = await ReadRequestAsync(pipe, lifetimeToken).ConfigureAwait(false);
                JsonElement helloRoot = hello.RootElement;
                if (!string.Equals(RequiredString(helloRoot, "type"), "hello", StringComparison.Ordinal) ||
                    !Authenticate(RequiredString(helloRoot, "token")))
                {
                    throw new TomodachiPipeProtocolException("authentication-failed");
                }

                await WriteResponseAsync(pipe, new Dictionary<string, object>
                {
                    ["protocol"] = TomodachiPipeProtocol.Version,
                    ["type"] = "hello.ack",
                    ["requestId"] = RequiredString(helloRoot, "requestId"),
                    ["providerInstanceId"] = _providerInstanceId,
                    ["capabilities"] = new Dictionary<string, object>
                    {
                        ["buttons"] = new[] { "A" },
                        ["actions"] = new[] { "press", "release" },
                        ["providerSampleReceipt"] = true,
                        ["allNeutralReceipt"] = true,
                        ["ownerStop"] = true,
                        ["heartbeat"] = true,
                    },
                    ["limits"] = new Dictionary<string, object>
                    {
                        ["maxFrameBytes"] = TomodachiPipeProtocol.MaxFrameBytes,
                        ["watchdogMs"] = 2000,
                    },
                }, lifetimeToken).ConfigureAwait(false);

                while (!lifetimeToken.IsCancellationRequested)
                {
                    using JsonDocument request = await ReadRequestAsync(pipe, lifetimeToken).ConfigureAwait(false);
                    JsonElement root = request.RootElement;
                    string type = RequiredString(root, "type");
                    string requestId = RequiredString(root, "requestId");
                    string traceId = RequiredString(root, "traceId");
                    Dictionary<string, object> response = new()
                    {
                        ["protocol"] = TomodachiPipeProtocol.Version,
                        ["type"] = $"{type}.ack",
                        ["requestId"] = requestId,
                        ["traceId"] = traceId,
                    };

                    switch (type)
                    {
                        case "arm":
                            HandleArm(root, response);
                            break;
                        case "heartbeat":
                            HandleHeartbeat(root, response);
                            break;
                        case "input":
                            HandleInput(root, response);
                            break;
                        case "receipt.get":
                            HandleReceipt(root, response);
                            break;
                        case "health":
                            HandleHealth(response);
                            break;
                        case "neutralize":
                            HandleNeutralize(root, response);
                            neutralized = true;
                            break;
                        default:
                            throw new TomodachiPipeProtocolException("unknown-request-type");
                    }

                    await WriteResponseAsync(pipe, response, lifetimeToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
            {
                disconnectReason = NeutralizeReason.ProviderDisposal;
                throw;
            }
            catch (OperationCanceledException)
            {
                disconnectReason = NeutralizeReason.ProtocolFailure;
            }
            catch (TomodachiPipeProtocolException exception)
            {
                disconnectReason = exception.Code == "authentication-failed"
                    ? NeutralizeReason.AuthenticationFailure
                    : NeutralizeReason.ProtocolFailure;
            }
            catch (JsonException)
            {
                disconnectReason = NeutralizeReason.ProtocolFailure;
            }
            catch (IOException)
            {
                disconnectReason = NeutralizeReason.Disconnected;
            }
            finally
            {
                if (!neutralized)
                {
                    _control.NeutralizeAndLatch($"ipc-{Guid.NewGuid():N}", disconnectReason);
                }
            }
        }

        private async ValueTask<JsonDocument> ReadRequestAsync(Stream pipe, CancellationToken lifetimeToken)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            timeout.CancelAfter(_options.RequestTimeout);
            byte[] payload = await TomodachiPipeProtocol.ReadFrameAsync(pipe, timeout.Token).ConfigureAwait(false);
            return TomodachiPipeProtocol.ParseRequest(payload);
        }

        private async ValueTask WriteResponseAsync(Stream pipe, object response, CancellationToken lifetimeToken)
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(response);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            timeout.CancelAfter(_options.RequestTimeout);
            await _sendLock.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                await TomodachiPipeProtocol.WriteFrameAsync(pipe, payload, timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private bool Authenticate(string encodedToken)
        {
            if (!TryDecodeBase64Url(encodedToken, out byte[] supplied))
            {
                return false;
            }

            return supplied.Length == _options.TokenBytes.Length &&
                CryptographicOperations.FixedTimeEquals(supplied, _options.TokenBytes);
        }


        private void HandleHeartbeat(JsonElement root, Dictionary<string, object> response)
        {
            response["observed"] = _control.ObserveBridgeHeartbeat(ReadAuthority(root));
        }

        private void HandleArm(JsonElement root, Dictionary<string, object> response)
        {
            ArmResult result = _control.Arm(RequiredString(root, "armId"), ReadAuthority(root));
            response["armed"] = result.Armed;
            response["detail"] = result.Detail;
        }

        private void HandleInput(JsonElement root, Dictionary<string, object> response)
        {
            string button = RequiredString(root, "button");
            string action = RequiredString(root, "action");
            if (!string.Equals(button, "A", StringComparison.Ordinal) ||
                (action != "press" && action != "release"))
            {
                throw new TomodachiPipeProtocolException("non-canonical-enum");
            }

            if (!root.TryGetProperty("sequence", out JsonElement sequenceElement) ||
                !sequenceElement.TryGetInt64(out long sequence) || sequence < 0)
            {
                throw new TomodachiPipeProtocolException("invalid-sequence");
            }

            if (!DateTimeOffset.TryParseExact(
                    RequiredString(root, "expiresAt"),
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset expiresAt) &&
                !DateTimeOffset.TryParse(
                    RequiredString(root, "expiresAt"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out expiresAt))
            {
                throw new TomodachiPipeProtocolException("invalid-timestamp");
            }

            TomodachiInputCommand command = new(
                RequiredString(root, "commandId"),
                ReadAuthority(root),
                sequence,
                expiresAt,
                GamepadButtonInputId.A,
                action == "press" ? TomodachiButtonAction.Press : TomodachiButtonAction.Release,
                RequiredString(root, "traceId"));
            ApplyResult result = _control.Apply(command);
            response["commandId"] = result.Receipt.CommandId;
            response["accepted"] = result.Accepted;
            response["duplicate"] = result.Duplicate;
            response["disposition"] = WireDisposition(result.Receipt.Disposition);
            response["sampled"] = result.Receipt.Sampled;
            response["detail"] = result.Receipt.Detail;
        }

        private void HandleReceipt(JsonElement root, Dictionary<string, object> response)
        {
            string commandId = RequiredString(root, "commandId");
            bool found = _control.TryGetCommandReceipt(commandId, out CommandReceipt receipt);
            response["commandId"] = commandId;
            response["found"] = found;
            if (found)
            {
                response["disposition"] = WireDisposition(receipt.Disposition);
                response["sampled"] = receipt.Sampled;
                response["providerPoll"] = receipt.ProviderPoll;
                response["sampledAt"] = receipt.SampledAt;
                response["detail"] = receipt.Detail;
                NeutralSampleReceipt? neutralReceipt = _control.GetLastAllNeutralSampleReceipt();
                bool allNeutral = receipt.Sampled &&
                    receipt.ProviderPoll.HasValue &&
                    neutralReceipt.HasValue &&
                    neutralReceipt.Value.AllNeutral &&
                    neutralReceipt.Value.ProviderPoll >= receipt.ProviderPoll.Value;
                response["allNeutral"] = allNeutral;
                if (allNeutral)
                {
                    response["allNeutralProviderPoll"] = neutralReceipt.Value.ProviderPoll;
                    response["allNeutralSampledAt"] = neutralReceipt.Value.SampledAt;
                    response["neutralGeneration"] = neutralReceipt.Value.NeutralGeneration;
                }
            }
        }

        private void HandleHealth(Dictionary<string, object> response)
        {
            ProviderHealth health = _control.GetHealth();
            response["enabled"] = health.Enabled;
            response["armed"] = health.Armed;
            response["ready"] = health.Ready;
            response["polling"] = health.Polling;
            response["stale"] = health.Stale;
            response["latched"] = health.Latched;
            response["disposed"] = health.Disposed;
            response["allNeutral"] = health.AllNeutral;
            response["queueDepth"] = health.QueueDepth;
            response["providerPoll"] = health.ProviderPoll;
            response["neutralGeneration"] = health.NeutralGeneration;
        }

        private void HandleNeutralize(JsonElement root, Dictionary<string, object> response)
        {
            if (!string.Equals(RequiredString(root, "reason"), "owner-stop", StringComparison.Ordinal))
            {
                throw new TomodachiPipeProtocolException("non-canonical-enum");
            }

            NeutralizeResult result = _control.NeutralizeAndLatch(
                RequiredString(root, "stopId"),
                NeutralizeReason.OwnerStop);
            response["latched"] = result.Latched;
            response["duplicate"] = result.Duplicate;
            response["neutralGeneration"] = result.NeutralGeneration;
            response["allNeutralSampled"] = result.AllNeutralSampled;
            response["detail"] = result.Detail;
        }

        private static TomodachiAuthorityEpoch ReadAuthority(JsonElement root)
        {
            if (!root.TryGetProperty("authority", out JsonElement authority))
            {
                throw new TomodachiPipeProtocolException("invalid-authority");
            }

            return new TomodachiAuthorityEpoch(
                RequiredString(authority, "serverInstanceId"),
                RequiredString(authority, "roomId"),
                RequiredString(authority, "controlLeaseId"),
                RequiredString(authority, "sessionId"));
        }

        private static string RequiredString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.String)
            {
                throw new TomodachiPipeProtocolException("invalid-request");
            }

            string value = element.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new TomodachiPipeProtocolException("invalid-request");
            }

            return value;
        }

        private static string WireDisposition(ApplyDisposition disposition)
        {
            return disposition switch
            {
                ApplyDisposition.Accepted => "accepted",
                ApplyDisposition.Duplicate => "duplicate",
                ApplyDisposition.Rejected => "rejected",
                ApplyDisposition.SupersededByNeutralize => "superseded-by-neutralize",
                _ => throw new TomodachiPipeProtocolException("invalid-provider-disposition"),
            };
        }

        private static bool TryDecodeBase64Url(string value, out byte[] decoded)
        {
            decoded = null;
            string padded = value.Replace('-', '+').Replace('_', '/');
            int remainder = padded.Length % 4;
            if (remainder == 1)
            {
                return false;
            }

            if (remainder != 0)
            {
                padded = padded.PadRight(padded.Length + (4 - remainder), '=');
            }

            try
            {
                decoded = Convert.FromBase64String(padded);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
