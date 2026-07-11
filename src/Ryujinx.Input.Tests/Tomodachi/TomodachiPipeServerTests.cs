using NUnit.Framework;
using Ryujinx.Input.Drivers;
using Ryujinx.Input.Tomodachi;
using Ryujinx.Input.Tomodachi.Ipc;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Input.Tests.Tomodachi
{
    [TestFixture]
    public class TomodachiPipeServerTests
    {
        private const string Authority = "\"authority\":{\"serverInstanceId\":\"server\",\"roomId\":\"room\",\"controlLeaseId\":\"lease\",\"sessionId\":\"session\"}";

        [Test]
        public async Task AuthenticatedConnectionExposesOnlyProviderSampledCorrelatedReceipts()
        {
            await using Harness harness = new();
            using NamedPipeClientStream client = await harness.ConnectAuthenticatedAsync();
            using JsonDocument arm = await ArmAsync(client, "arm-1");
            Assert.That(arm.RootElement.GetProperty("armed").GetBoolean(), Is.True);

            using JsonDocument pressAccepted = await InputAsync(client, "press-1", 2, "press");
            Assert.Multiple(() =>
            {
                Assert.That(pressAccepted.RootElement.GetProperty("accepted").GetBoolean(), Is.True);
                Assert.That(pressAccepted.RootElement.GetProperty("sampled").GetBoolean(), Is.False);
                Assert.That(pressAccepted.RootElement.TryGetProperty("applied", out _), Is.False);
            });

            using JsonDocument releaseAccepted = await InputAsync(client, "release-1", 3, "release");
            PollResult pressed = harness.State.PollMappedSnapshot();
            PollResult released = harness.State.PollMappedSnapshot();
            Assert.Multiple(() =>
            {
                Assert.That(pressed.Snapshot.IsPressed(GamepadButtonInputId.A), Is.True);
                Assert.That(released.Snapshot.IsPressed(GamepadButtonInputId.A), Is.False);
                Assert.That(released.ProviderPoll, Is.EqualTo(pressed.ProviderPoll + 1));
                Assert.That(releaseAccepted.RootElement.GetProperty("sampled").GetBoolean(), Is.False);
            });

            using JsonDocument pressReceipt = await ReceiptAsync(client, "press-1");
            using JsonDocument releaseReceipt = await ReceiptAsync(client, "release-1");
            Assert.Multiple(() =>
            {
                Assert.That(pressReceipt.RootElement.GetProperty("commandId").GetString(), Is.EqualTo("press-1"));
                Assert.That(pressReceipt.RootElement.GetProperty("sampled").GetBoolean(), Is.True);
                Assert.That(pressReceipt.RootElement.GetProperty("providerPoll").GetInt64(), Is.EqualTo(pressed.ProviderPoll));
                Assert.That(releaseReceipt.RootElement.GetProperty("commandId").GetString(), Is.EqualTo("release-1"));
                Assert.That(releaseReceipt.RootElement.GetProperty("sampled").GetBoolean(), Is.True);
                Assert.That(releaseReceipt.RootElement.GetProperty("providerPoll").GetInt64(), Is.EqualTo(released.ProviderPoll));
                Assert.That(releaseReceipt.RootElement.GetProperty("allNeutral").GetBoolean(), Is.True);
                Assert.That(releaseReceipt.RootElement.GetProperty("allNeutralProviderPoll").GetInt64(), Is.EqualTo(released.ProviderPoll));
                Assert.That(releaseReceipt.RootElement.TryGetProperty("hleApplied", out _), Is.False);
                Assert.That(releaseReceipt.RootElement.TryGetProperty("gameObserved", out _), Is.False);
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task WrongTokenOrVersionCannotMutateAndLatches(bool wrongToken)
        {
            await using Harness harness = new();
            using NamedPipeClientStream client = await harness.ConnectAsync();
            string token = wrongToken ? Harness.OtherToken : harness.Token;
            string protocol = wrongToken ? TomodachiPipeProtocol.Version : "tomodachi-ipc/999";
            await SendAsync(client, $"{{\"protocol\":\"{protocol}\",\"type\":\"hello\",\"requestId\":\"hello-1\",\"clientInstanceId\":\"bridge-instance\",\"token\":\"{token}\",\"sentAt\":\"2026-01-01T00:00:00Z\"}}");

            await WaitForAsync(() => harness.State.GetHealth().Latched);
            Assert.Multiple(() =>
            {
                Assert.That(harness.State.GetHealth().Armed, Is.False);
                Assert.That(harness.State.GetHealth().AllNeutral, Is.True);
                Assert.That(harness.State.GetHealth().Latched, Is.True);
            });
        }

        [Test]
        public async Task DuplicateOrUnknownJsonAfterArmNeutralizesAndLatches()
        {
            await AssertProtocolFailureLatchesAsync("{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"health\",\"requestId\":\"r\",\"requestId\":\"duplicate\",\"traceId\":\"t\",\"sentAt\":\"2026-01-01T00:00:00Z\"}");
            await AssertProtocolFailureLatchesAsync("{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"health\",\"requestId\":\"r\",\"traceId\":\"t\",\"sentAt\":\"2026-01-01T00:00:00Z\",\"unknown\":true}");
        }

        [Test]
        public async Task OversizeAndTruncatedFramesAfterArmNeutralizeAndLatch()
        {
            await using (Harness oversize = new())
            {
                using NamedPipeClientStream client = await oversize.ConnectAuthenticatedAsync();
                using JsonDocument arm = await ArmAsync(client, "arm-oversize");
                byte[] prefix = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(prefix, TomodachiPipeProtocol.MaxFrameBytes + 1);
                await client.WriteAsync(prefix);
                await client.FlushAsync();
                await WaitForAsync(() => oversize.State.GetHealth().Latched);
            }

            await using (Harness truncated = new())
            {
                NamedPipeClientStream client = await truncated.ConnectAuthenticatedAsync();
                using JsonDocument arm = await ArmAsync(client, "arm-truncated");
                byte[] prefix = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(prefix, 20);
                await client.WriteAsync(prefix);
                await client.WriteAsync(new byte[] { (byte)'{' });
                await client.FlushAsync();
                client.Dispose();
                await WaitForAsync(() => truncated.State.GetHealth().Latched);
            }
        }

        [Test]
        public async Task DisconnectWhilePressedNeutralizesAndReconnectRequiresNewArmId()
        {
            await using Harness harness = new();
            using (NamedPipeClientStream first = await harness.ConnectAuthenticatedAsync())
            {
                using JsonDocument arm = await ArmAsync(first, "arm-1");
                using JsonDocument input = await InputAsync(first, "press-1", 1, "press");
                Assert.That(harness.State.PollMappedSnapshot().Snapshot.IsPressed(GamepadButtonInputId.A), Is.True);
            }

            await WaitForAsync(() => harness.State.GetHealth().Latched);
            Assert.That(harness.State.GetHealth().AllNeutral, Is.True);

            using NamedPipeClientStream second = await harness.ConnectAuthenticatedAsync();
            using JsonDocument repeated = await ArmAsync(second, "arm-1");
            using JsonDocument fresh = await ArmAsync(second, "arm-2");
            Assert.Multiple(() =>
            {
                Assert.That(repeated.RootElement.GetProperty("armed").GetBoolean(), Is.False);
                Assert.That(repeated.RootElement.GetProperty("detail").GetString(), Is.EqualTo("new-arm-required"));
                Assert.That(fresh.RootElement.GetProperty("armed").GetBoolean(), Is.True);
            });
        }

        [Test]
        public async Task OwnerStopIsIdempotentAndReportsProviderSampledNeutrality()
        {
            await using Harness harness = new();
            using NamedPipeClientStream client = await harness.ConnectAuthenticatedAsync();
            using JsonDocument arm = await ArmAsync(client, "arm-owner");
            using JsonDocument input = await InputAsync(client, "press-owner", 1, "press");
            harness.State.PollMappedSnapshot();

            using JsonDocument first = await NeutralizeAsync(client, "stop-1");
            PollResult neutral = harness.State.PollMappedSnapshot();
            using JsonDocument duplicate = await NeutralizeAsync(client, "stop-1");

            Assert.Multiple(() =>
            {
                Assert.That(first.RootElement.GetProperty("duplicate").GetBoolean(), Is.False);
                Assert.That(first.RootElement.GetProperty("allNeutralSampled").GetBoolean(), Is.False);
                Assert.That(neutral.AllNeutralReceipt.HasValue, Is.True);
                Assert.That(duplicate.RootElement.GetProperty("duplicate").GetBoolean(), Is.True);
                Assert.That(duplicate.RootElement.GetProperty("allNeutralSampled").GetBoolean(), Is.True);
                Assert.That(duplicate.RootElement.GetProperty("neutralGeneration").GetInt64(), Is.EqualTo(neutral.NeutralGeneration));
            });
        }

        [Test]
        public async Task SecondServerCollisionDoesNotDisableFirstServer()
        {
            string pipeName = $"tomodachi-live-{Guid.NewGuid():N}";
            await using Harness first = new(pipeName);
            Dictionary<string, string> environment = TomodachiPipeOptionsTests.ValidEnvironment(pipeName);
            Assert.That(TomodachiPipeOptions.TryLoad(environment.GetValueOrDefault, ["ryujinx"], out TomodachiPipeOptions options, out _), Is.True);
            using TomodachiInputState secondState = new();

            Assert.Throws<IOException>(() => TomodachiPipeServer.Start(options, secondState));
            using NamedPipeClientStream client = await first.ConnectAuthenticatedAsync();
            using JsonDocument health = await RequestAsync(client, "{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"health\",\"requestId\":\"health-1\",\"traceId\":\"trace-health\",\"sentAt\":\"2026-01-01T00:00:00Z\"}");
            Assert.That(health.RootElement.GetProperty("type").GetString(), Is.EqualTo("health.ack"));
        }

        [Test]
        public async Task ServerDisposalCancelsActiveReadAndLeavesProviderNeutralLatched()
        {
            Harness harness = new();
            using NamedPipeClientStream client = await harness.ConnectAuthenticatedAsync();
            using JsonDocument arm = await ArmAsync(client, "arm-dispose");
            using JsonDocument input = await InputAsync(client, "press-dispose", 1, "press");
            harness.State.PollMappedSnapshot();

            await harness.Server.DisposeAsync();
            await WaitForAsync(() => harness.State.GetHealth().Latched);

            Assert.Multiple(() =>
            {
                Assert.That(harness.State.GetHealth().AllNeutral, Is.True);
                Assert.That(harness.State.GetHealth().Latched, Is.True);
            });
            harness.DisposeStateOnly();
        }

        [Test]
        public async Task ManagedChildProcessServesSampledPulseAndCleansUp()
        {
            string configuration = Directory.GetParent(TestContext.CurrentContext.TestDirectory)?.Name ?? "Debug";
            string helperPath = Path.GetFullPath(
                Path.Combine(TestContext.CurrentContext.TestDirectory, $"../../../../Ryujinx.Input.Ipc.TestHost/bin/{configuration}/net10.0/Ryujinx.Input.Ipc.TestHost.dll"));
            Assert.That(File.Exists(helperPath), Is.True, $"Missing managed test host: {helperPath}");
            Dictionary<string, string> environment = TomodachiPipeOptionsTests.ValidEnvironment();
            string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "/home/angan/.dotnet10/dotnet";
            ProcessStartInfo startInfo = new(dotnetHost)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(helperPath);
            foreach (KeyValuePair<string, string> entry in environment)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }

            using Process process = Process.Start(startInfo);
            try
            {
                string ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(ready, Is.EqualTo("READY"));
                using NamedPipeClientStream client = new(".", environment[TomodachiPipeOptions.PipeNameEnvironmentVariable], PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(2000);
                string token = environment[TomodachiPipeOptions.PipeTokenEnvironmentVariable];
                using JsonDocument hello = await RequestAsync(client, $"{{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"hello\",\"requestId\":\"hello-child\",\"clientInstanceId\":\"bridge-child\",\"token\":\"{token}\",\"sentAt\":\"2026-01-01T00:00:00Z\"}}");
                using JsonDocument arm = await ArmAsync(client, "arm-child");
                using JsonDocument press = await InputAsync(client, "child:press", 10, "press");
                using JsonDocument release = await InputAsync(client, "child:release", 11, "release");

                JsonDocument pressReceipt = null;
                JsonDocument releaseReceipt = null;
                using CancellationTokenSource receiptTimeout = new(TimeSpan.FromSeconds(3));
                while (!receiptTimeout.IsCancellationRequested)
                {
                    pressReceipt?.Dispose();
                    releaseReceipt?.Dispose();
                    pressReceipt = await ReceiptAsync(client, "child:press");
                    releaseReceipt = await ReceiptAsync(client, "child:release");
                    if (pressReceipt.RootElement.GetProperty("sampled").GetBoolean() &&
                        releaseReceipt.RootElement.GetProperty("sampled").GetBoolean() &&
                        releaseReceipt.RootElement.GetProperty("allNeutral").GetBoolean())
                    {
                        break;
                    }

                    await Task.Delay(10, receiptTimeout.Token);
                }

                using (pressReceipt)
                using (releaseReceipt)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(pressReceipt.RootElement.GetProperty("sampled").GetBoolean(), Is.True);
                        Assert.That(releaseReceipt.RootElement.GetProperty("sampled").GetBoolean(), Is.True);
                        Assert.That(releaseReceipt.RootElement.GetProperty("providerPoll").GetInt64(),
                            Is.GreaterThan(pressReceipt.RootElement.GetProperty("providerPoll").GetInt64()));
                        Assert.That(releaseReceipt.RootElement.GetProperty("allNeutral").GetBoolean(), Is.True);
                    });
                }

                await process.StandardInput.WriteLineAsync("STOP");
                process.StandardInput.Close();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(process.ExitCode, Is.Zero, await process.StandardError.ReadToEndAsync());
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }

        [Test]
        public async Task HeartbeatReportsOnlyExactAuthorityObservationAndHealthIsRedacted()
        {
            await using Harness harness = new();
            using NamedPipeClientStream client = await harness.ConnectAuthenticatedAsync();
            using JsonDocument arm = await ArmAsync(client, "arm-heartbeat");
            using JsonDocument wrong = await RequestAsync(client, "{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"heartbeat\",\"requestId\":\"heartbeat-wrong\",\"traceId\":\"trace-heartbeat\",\"sentAt\":\"2026-01-01T00:00:00Z\",\"authority\":{\"serverInstanceId\":\"server\",\"roomId\":\"wrong-room\",\"controlLeaseId\":\"lease\",\"sessionId\":\"session\"}}");
            using JsonDocument exact = await RequestAsync(client, $"{{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"heartbeat\",\"requestId\":\"heartbeat-exact\",\"traceId\":\"trace-heartbeat\",\"sentAt\":\"2026-01-01T00:00:00Z\",{Authority}}}");
            using JsonDocument health = await RequestAsync(client, "{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"health\",\"requestId\":\"health-redacted\",\"traceId\":\"trace-health\",\"sentAt\":\"2026-01-01T00:00:00Z\"}");
            string healthJson = health.RootElement.GetRawText();

            Assert.Multiple(() =>
            {
                Assert.That(wrong.RootElement.GetProperty("observed").GetBoolean(), Is.False);
                Assert.That(exact.RootElement.GetProperty("observed").GetBoolean(), Is.True);
                Assert.That(healthJson, Does.Not.Contain(harness.Token));
                Assert.That(healthJson, Does.Not.Contain(harness.PipeName));
                Assert.That(healthJson, Does.Not.Contain("wrong-room"));
                Assert.That(health.RootElement.GetProperty("latched").GetBoolean(), Is.False);
            });
        }

        [Test]
        public async Task NonCanonicalInputEnumIsProtocolFailureAndNeutralizes()
        {
            await using Harness harness = new();
            using NamedPipeClientStream client = await harness.ConnectAuthenticatedAsync();
            using JsonDocument arm = await ArmAsync(client, "arm-enum");
            await SendAsync(client, $"{{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"input\",\"requestId\":\"input-bad\",\"traceId\":\"trace-bad\",\"sentAt\":\"2026-01-01T00:00:00Z\",\"commandId\":\"bad\",{Authority},\"sequence\":1,\"expiresAt\":\"2999-01-01T00:00:00Z\",\"button\":\"a\",\"action\":\"PRESS\"}}");

            await WaitForAsync(() => harness.State.GetHealth().Latched);
            Assert.That(harness.State.GetHealth().AllNeutral, Is.True);
        }

        private static async Task AssertProtocolFailureLatchesAsync(string request)
        {
            await using Harness harness = new();
            using NamedPipeClientStream client = await harness.ConnectAuthenticatedAsync();
            using JsonDocument arm = await ArmAsync(client, $"arm-{Guid.NewGuid():N}");
            await SendAsync(client, request);
            await WaitForAsync(() => harness.State.GetHealth().Latched);
            Assert.That(harness.State.GetHealth().AllNeutral, Is.True);
        }

        private static Task<JsonDocument> ArmAsync(NamedPipeClientStream client, string armId) =>
            RequestAsync(client, $"{{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"arm\",\"requestId\":\"arm-request\",\"traceId\":\"trace-arm\",\"sentAt\":\"2026-01-01T00:00:00Z\",\"armId\":\"{armId}\",{Authority}}}");

        private static Task<JsonDocument> InputAsync(NamedPipeClientStream client, string commandId, long sequence, string action) =>
            RequestAsync(client, $"{{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"input\",\"requestId\":\"input-{commandId}\",\"traceId\":\"trace-{commandId}\",\"sentAt\":\"2026-01-01T00:00:00Z\",\"commandId\":\"{commandId}\",{Authority},\"sequence\":{sequence},\"expiresAt\":\"2999-01-01T00:00:00Z\",\"button\":\"A\",\"action\":\"{action}\"}}");

        private static Task<JsonDocument> ReceiptAsync(NamedPipeClientStream client, string commandId) =>
            RequestAsync(client, $"{{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"receipt.get\",\"requestId\":\"receipt-{commandId}\",\"traceId\":\"trace-receipt\",\"sentAt\":\"2026-01-01T00:00:00Z\",\"commandId\":\"{commandId}\"}}");

        private static Task<JsonDocument> NeutralizeAsync(NamedPipeClientStream client, string stopId) =>
            RequestAsync(client, $"{{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"neutralize\",\"requestId\":\"neutral-{stopId}\",\"traceId\":\"trace-neutral\",\"sentAt\":\"2026-01-01T00:00:00Z\",\"stopId\":\"{stopId}\",\"reason\":\"owner-stop\"}}");

        private static async Task<JsonDocument> RequestAsync(NamedPipeClientStream client, string json)
        {
            await SendAsync(client, json);
            return await ReceiveAsync(client);
        }

        private static async Task SendAsync(NamedPipeClientStream client, string json)
        {
            await TomodachiPipeProtocol.WriteFrameAsync(client, Encoding.UTF8.GetBytes(json), CancellationToken.None);
        }

        private static async Task<JsonDocument> ReceiveAsync(NamedPipeClientStream client)
        {
            byte[] payload = await TomodachiPipeProtocol.ReadFrameAsync(client, CancellationToken.None);
            return JsonDocument.Parse(payload);
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            while (!condition())
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        private sealed class Harness : IAsyncDisposable
        {
            public static string OtherToken
            {
                get
                {
                    byte[] bytes = new byte[32];
                    bytes[0] = 1;
                    return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                }
            }
            private bool _stateDisposed;

            public string PipeName { get; }
            public string Token { get; }
            public TomodachiInputState State { get; }
            public TomodachiPipeServer Server { get; }

            public Harness(string pipeName = null)
            {
                PipeName = pipeName ?? $"tomodachi-live-{Guid.NewGuid():N}";
                Dictionary<string, string> environment = TomodachiPipeOptionsTests.ValidEnvironment(PipeName);
                Token = environment[TomodachiPipeOptions.PipeTokenEnvironmentVariable];
                Assert.That(TomodachiPipeOptions.TryLoad(environment.GetValueOrDefault, ["ryujinx"], out TomodachiPipeOptions options, out _), Is.True);
                State = new TomodachiInputState();
                Server = TomodachiPipeServer.Start(options, State);
            }

            public async Task<NamedPipeClientStream> ConnectAsync()
            {
                NamedPipeClientStream client = new(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(2000);
                return client;
            }

            public async Task<NamedPipeClientStream> ConnectAuthenticatedAsync()
            {
                NamedPipeClientStream client = await ConnectAsync();
                await SendAsync(client, $"{{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"hello\",\"requestId\":\"hello-1\",\"clientInstanceId\":\"bridge-instance\",\"token\":\"{Token}\",\"sentAt\":\"2026-01-01T00:00:00Z\"}}");
                using JsonDocument hello = await ReceiveAsync(client);
                Assert.That(hello.RootElement.GetProperty("type").GetString(), Is.EqualTo("hello.ack"));
                return client;
            }

            public void DisposeStateOnly()
            {
                if (!_stateDisposed)
                {
                    _stateDisposed = true;
                    State.Dispose();
                }
            }

            public async ValueTask DisposeAsync()
            {
                await Server.DisposeAsync();
                DisposeStateOnly();
            }
        }
    }
}
