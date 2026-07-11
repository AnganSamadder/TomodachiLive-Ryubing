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
        string IpcStatus);

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
                return new TomodachiInputBootstrapResult(primaryDriver, null, null, "provider-disabled");
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
                        ipcLifetime = TomodachiPipeServer.Start(pipeOptions, state);
                        ipcStatus = "listening";
                    }
                    catch (IOException)
                    {
                        ipcStatus = "pipe-name-in-use";
                    }
                    catch (UnauthorizedAccessException)
                    {
                        ipcStatus = "pipe-unavailable";
                    }
                    catch (PlatformNotSupportedException)
                    {
                        ipcStatus = "platform-unsupported";
                    }
                }

                return new TomodachiInputBootstrapResult(composite, state, ipcLifetime, ipcStatus);
            }
            catch
            {
                state.Dispose();
                throw;
            }
        }
    }
}
