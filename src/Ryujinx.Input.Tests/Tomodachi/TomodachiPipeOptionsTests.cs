using NUnit.Framework;
using Ryujinx.Input.Drivers;
using Ryujinx.Input.Tomodachi;
using Ryujinx.Input.Tomodachi.Ipc;
using System;
using System.Collections.Generic;
using System.IO;

namespace Ryujinx.Input.Tests.Tomodachi
{
    [TestFixture]
    public class TomodachiPipeOptionsTests
    {
        [Test]
        public void MissingConfigurationLeavesIpcDisabledWithoutInventingCredentials()
        {
            Dictionary<string, string> environment = new(StringComparer.Ordinal);

            bool loaded = TomodachiPipeOptions.TryLoad(
                environment.GetValueOrDefault,
                ["ryujinx"],
                out TomodachiPipeOptions options,
                out string disabledReason);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.False);
                Assert.That(options, Is.Null);
                Assert.That(disabledReason, Is.EqualTo("missing-configuration"));
            });
        }

        [TestCase("--ryubing-pipe-token=secret")]
        [TestCase("--pipe-token", "secret")]
        [TestCase("--tomodachi-ryubing-pipe-token=secret")]
        [TestCase("--ryubing-pipe-name=guessable")]
        [TestCase("--pipe-name", "guessable")]
        [TestCase("--tomodachi-ryubing-pipe-name=guessable")]
        public void CommandLineIpcCredentialsAreRejected(params string[] arguments)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                TomodachiPipeOptions.TryLoad(_ => null, arguments, out _, out _));

            Assert.That(exception.Message, Does.Contain("ipc-credential-cli-rejected"));
        }

        [Test]
        public void ValidEnvironmentConfigurationLoadsWithoutExposingToken()
        {
            Dictionary<string, string> environment = ValidEnvironment();

            bool loaded = TomodachiPipeOptions.TryLoad(
                environment.GetValueOrDefault,
                ["ryujinx"],
                out TomodachiPipeOptions options,
                out string disabledReason);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.True);
                Assert.That(options.PipeName, Does.StartWith("tomodachi-live-"));
                Assert.That(options.RequestTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
                Assert.That(options.ToString(), Does.Not.Contain(environment[TomodachiPipeOptions.PipeTokenEnvironmentVariable]));
                Assert.That(disabledReason, Is.Null);
            });
        }

        [Test]
        public void StatusProofIdentityConfigurationIsEnvironmentOnlyAndBootstrapExposesAuthority()
        {
            string localSavePath = Directory.CreateTempSubdirectory("tomodachi-config-").FullName;
            try
            {
                Dictionary<string, string> environment = ValidEnvironment();
                environment[TomodachiPipeOptions.StatusProofIdentitySavePathEnvironmentVariable] = "/mnt/c/private/save";
                environment[TomodachiPipeOptions.StatusProofLocalSavePathEnvironmentVariable] = localSavePath;
                environment[TomodachiPipeOptions.StatusProofActiveSessionPathEnvironmentVariable] = "/mnt/c/private/session.json";

                Assert.That(TomodachiPipeOptions.TryLoad(
                    environment.GetValueOrDefault,
                    ["ryujinx"],
                    out TomodachiPipeOptions options,
                    out string disabledReason), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(options.StatusProofIdentitySavePath, Is.EqualTo("/mnt/c/private/save"));
                    Assert.That(options.StatusProofLocalSavePath, Is.EqualTo(localSavePath));
                    Assert.That(options.StatusProofActiveSessionPath, Is.EqualTo("/mnt/c/private/session.json"));
                    Assert.That(disabledReason, Is.Null);
                });

                using EmptyGamepadDriver primary = new();
                TomodachiInputBootstrapResult result = TomodachiInputBootstrap.CreateGamepadDriver(
                    primary,
                    enableProvider: true,
                    getEnvironmentVariable: environment.GetValueOrDefault,
                    commandLineArguments: ["ryujinx"]);
                try
                {
                    Assert.That(result.StatusProofAuthority, Is.Not.Null);
                    Assert.That(result.StatusProofAuthority.TrySample(out _), Is.False);
                }
                finally
                {
                    result.IpcLifetime?.Dispose();
                    result.StatusProofAuthority?.Dispose();
                    result.GamepadDriver.Dispose();
                }
            }
            finally
            {
                Directory.Delete(localSavePath, recursive: true);
            }
        }

        [Test]
        public void InvalidLocalStatusProofPathDisablesIpcWithoutCrashingBootstrap()
        {
            Dictionary<string, string> environment = ValidEnvironment();
            environment[TomodachiPipeOptions.StatusProofIdentitySavePathEnvironmentVariable] = "/private/save";
            environment[TomodachiPipeOptions.StatusProofLocalSavePathEnvironmentVariable] = "invalid\0path";

            using EmptyGamepadDriver primary = new();
            TomodachiInputBootstrapResult result = TomodachiInputBootstrap.CreateGamepadDriver(
                primary,
                enableProvider: true,
                getEnvironmentVariable: environment.GetValueOrDefault,
                commandLineArguments: ["ryujinx"]);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result.IpcLifetime, Is.Null);
                    Assert.That(result.StatusProofAuthority, Is.Null);
                    Assert.That(result.IpcStatus, Is.EqualTo("invalid-configuration"));
                });
            }
            finally
            {
                result.GamepadDriver.Dispose();
            }
        }

        [Test]
        public void ActiveSessionIdentityWithoutSaveIdentityIsRejected()
        {
            Dictionary<string, string> environment = ValidEnvironment();
            environment[TomodachiPipeOptions.StatusProofActiveSessionPathEnvironmentVariable] = "/private/session.json";

            Assert.That(TomodachiPipeOptions.TryLoad(
                environment.GetValueOrDefault,
                ["ryujinx"],
                out TomodachiPipeOptions options,
                out string disabledReason), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(options, Is.Null);
                Assert.That(disabledReason, Is.EqualTo("invalid-configuration"));
            });
        }

        [Test]
        public void DisabledProviderPreservesPrimaryDriverAndNeverStartsIpc()
        {
            using EmptyGamepadDriver primary = new();
            TomodachiInputBootstrapResult result = TomodachiInputBootstrap.CreateGamepadDriver(
                primary,
                enableProvider: false,
                getEnvironmentVariable: ValidEnvironment().GetValueOrDefault,
                commandLineArguments: ["ryujinx"]);

            Assert.Multiple(() =>
            {
                Assert.That(result.GamepadDriver, Is.SameAs(primary));
                Assert.That(result.InputControl, Is.Null);
                Assert.That(result.IpcLifetime, Is.Null);
                Assert.That(result.IpcStatus, Is.EqualTo("provider-disabled"));
            });
        }

        internal static Dictionary<string, string> ValidEnvironment(string pipeName = null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TomodachiPipeOptions.PipeNameEnvironmentVariable] = pipeName ?? $"tomodachi-live-{Guid.NewGuid():N}",
                [TomodachiPipeOptions.PipeTokenEnvironmentVariable] = Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                [TomodachiPipeOptions.RequestTimeoutEnvironmentVariable] = "1500",
            };
        }

        private sealed class EmptyGamepadDriver : IGamepadDriver
        {
            public string DriverName => "empty";
            public ReadOnlySpan<string> GamepadsIds => [];
            public event Action<string> OnGamepadConnected { add { } remove { } }
            public event Action<string> OnGamepadDisconnected { add { } remove { } }
            public IGamepad GetGamepad(string id) => null;
            public IEnumerable<IGamepad> GetGamepads() => [];
            public void Clear() { }
            public void Dispose() { }
        }
    }
}
