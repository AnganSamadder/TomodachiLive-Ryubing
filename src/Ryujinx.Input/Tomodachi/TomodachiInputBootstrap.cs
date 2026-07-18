using Ryujinx.Input.Drivers;
using Ryujinx.Input.Tomodachi.Ipc;
using System;
using System.Collections.Generic;
using System.IO;

namespace Ryujinx.Input.Tomodachi
{
    public readonly record struct TomodachiInputBootstrapResult(
        IGamepadDriver GamepadDriver,
        ITomodachiInputControl InputControl,
        IDisposable IpcLifetime,
        string IpcStatus,
        TomodachiStatusProofAuthority StatusProofAuthority);

    public static class TomodachiInputBootstrap
    {
        public static TomodachiInputBootstrapResult CreateGamepadDriver(
            IGamepadDriver primaryDriver,
            bool enableProvider = false,
            TimeProvider timeProvider = null,
            int maxPendingTransitions = 64,
            int maxCommandResults = 1024,
            int maxStopResults = 256,
            TimeSpan? watchdogTimeout = null,
            Func<string, string> getEnvironmentVariable = null,
            IReadOnlyList<string> commandLineArguments = null)
        {
            ArgumentNullException.ThrowIfNull(primaryDriver);

            if (!enableProvider)
            {
                return new TomodachiInputBootstrapResult(primaryDriver, null, null, "provider-disabled", null);
            }

            TomodachiInputState state = new(
                timeProvider,
                maxPendingTransitions,
                maxCommandResults,
                maxStopResults,
                watchdogTimeout);
            try
            {
                TomodachiGamepadDriver virtualDriver = new(state);
                CompositeGamepadDriver composite = new(primaryDriver, virtualDriver);
                IDisposable ipcLifetime = null;
                TomodachiStatusProofAuthority statusProofAuthority = null;
                string ipcStatus;
                bool configured = TomodachiPipeOptions.TryLoad(
                    getEnvironmentVariable ?? Environment.GetEnvironmentVariable,
                    commandLineArguments ?? Environment.GetCommandLineArgs(),
                    out TomodachiPipeOptions pipeOptions,
                    out ipcStatus);
                if (configured)
                {
                    try
                    {
                        if (pipeOptions.StatusProofIdentitySavePath is not null)
                        {
                            statusProofAuthority = new TomodachiStatusProofAuthority(
                                pipeOptions.StatusProofIdentitySavePath,
                                pipeOptions.StatusProofLocalSavePath,
                                pipeOptions.StatusProofActiveSessionPath,
                                timeProvider);
                        }

                        ipcLifetime = TomodachiPipeServer.Start(pipeOptions, state, statusProofAuthority);
                        ipcStatus = "listening";
                    }
                    catch (IOException)
                    {
                        statusProofAuthority?.Dispose();
                        statusProofAuthority = null;
                        ipcStatus = "pipe-name-in-use";
                    }
                    catch (UnauthorizedAccessException)
                    {
                        statusProofAuthority?.Dispose();
                        statusProofAuthority = null;
                        ipcStatus = "pipe-unavailable";
                    }
                    catch (PlatformNotSupportedException)
                    {
                        statusProofAuthority?.Dispose();
                        statusProofAuthority = null;
                        ipcStatus = "platform-unsupported";
                    }
                    catch (ArgumentException)
                    {
                        statusProofAuthority?.Dispose();
                        statusProofAuthority = null;
                        ipcStatus = "invalid-configuration";
                    }
                    catch (NotSupportedException)
                    {
                        statusProofAuthority?.Dispose();
                        statusProofAuthority = null;
                        ipcStatus = "invalid-configuration";
                    }
                }

                return new TomodachiInputBootstrapResult(composite, state, ipcLifetime, ipcStatus, statusProofAuthority);
            }
            catch
            {
                state.Dispose();
                throw;
            }
        }
    }
}
