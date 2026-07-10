using Ryujinx.Input.Drivers;
using System;

namespace Ryujinx.Input.Tomodachi
{
    public readonly record struct TomodachiInputBootstrapResult(
        IGamepadDriver GamepadDriver,
        ITomodachiInputControl InputControl);

    public static class TomodachiInputBootstrap
    {
        public static TomodachiInputBootstrapResult CreateGamepadDriver(
            IGamepadDriver primaryDriver,
            bool enableProvider = false,
            TimeProvider timeProvider = null,
            int maxPendingTransitions = 64,
            int maxCommandResults = 1024,
            int maxStopResults = 256,
            TimeSpan? watchdogTimeout = null)
        {
            ArgumentNullException.ThrowIfNull(primaryDriver);

            if (!enableProvider)
            {
                return new TomodachiInputBootstrapResult(primaryDriver, null);
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
                return new TomodachiInputBootstrapResult(composite, state);
            }
            catch
            {
                state.Dispose();
                throw;
            }
        }
    }
}
