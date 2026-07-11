using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ryujinx.Input.Tomodachi.Ipc
{
    public sealed class TomodachiPipeOptions
    {
        private const int MaxStringBytes = 512;
        public const string PipeNameEnvironmentVariable = "TOMODACHI_RYUBING_PIPE_NAME";
        public const string PipeTokenEnvironmentVariable = "TOMODACHI_RYUBING_PIPE_TOKEN";
        public const string RequestTimeoutEnvironmentVariable = "TOMODACHI_RYUBING_PIPE_REQUEST_TIMEOUT_MS";
        public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);

        private readonly byte[] _tokenBytes;

        public string PipeName { get; }
        public TimeSpan RequestTimeout { get; }
        internal ReadOnlySpan<byte> TokenBytes => _tokenBytes;

        private TomodachiPipeOptions(string pipeName, byte[] tokenBytes, TimeSpan requestTimeout)
        {
            PipeName = pipeName;
            _tokenBytes = tokenBytes;
            RequestTimeout = requestTimeout;
        }

        public static bool TryLoad(
            Func<string, string> getEnvironmentVariable,
            IReadOnlyList<string> commandLineArguments,
            out TomodachiPipeOptions options,
            out string disabledReason)
        {
            ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
            ArgumentNullException.ThrowIfNull(commandLineArguments);

            RejectCommandLineCredentials(commandLineArguments);

            string pipeName = getEnvironmentVariable(PipeNameEnvironmentVariable);
            string token = getEnvironmentVariable(PipeTokenEnvironmentVariable);
            if (string.IsNullOrEmpty(pipeName) || string.IsNullOrEmpty(token))
            {
                options = null;
                disabledReason = "missing-configuration";
                return false;
            }

            if (!IsValidPipeName(pipeName) || !TryDecodeToken(token, out byte[] tokenBytes))
            {
                options = null;
                disabledReason = "invalid-configuration";
                return false;
            }

            TimeSpan requestTimeout = DefaultRequestTimeout;
            string timeoutText = getEnvironmentVariable(RequestTimeoutEnvironmentVariable);
            if (!string.IsNullOrEmpty(timeoutText))
            {
                if (!int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out int timeoutMs) ||
                    timeoutMs < 100 || timeoutMs > 30_000)
                {
                    options = null;
                    disabledReason = "invalid-configuration";
                    return false;
                }

                requestTimeout = TimeSpan.FromMilliseconds(timeoutMs);
            }

            options = new TomodachiPipeOptions(pipeName, tokenBytes, requestTimeout);
            disabledReason = null;
            return true;
        }

        public static bool TryLoadFromProcess(out TomodachiPipeOptions options, out string disabledReason)
        {
            return TryLoad(Environment.GetEnvironmentVariable, Environment.GetCommandLineArgs(), out options, out disabledReason);
        }

        private static void RejectCommandLineCredentials(IReadOnlyList<string> arguments)
        {
            foreach (string argument in arguments)
            {
                if (argument is null)
                {
                    continue;
                }

                string option = argument.Split('=', 2)[0];
                if (option.Equals("--ryubing-pipe-token", StringComparison.OrdinalIgnoreCase) ||
                    option.Equals("--pipe-token", StringComparison.OrdinalIgnoreCase) ||
                    option.Equals("--tomodachi-ryubing-pipe-token", StringComparison.OrdinalIgnoreCase) ||
                    option.Equals("--ryubing-pipe-name", StringComparison.OrdinalIgnoreCase) ||
                    option.Equals("--pipe-name", StringComparison.OrdinalIgnoreCase) ||
                    option.Equals("--tomodachi-ryubing-pipe-name", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("ipc-credential-cli-rejected", nameof(arguments));
                }
            }
        }

        private static bool IsValidPipeName(string pipeName)
        {
            int byteCount = Encoding.UTF8.GetByteCount(pipeName);
            if (byteCount < 32 || byteCount > MaxStringBytes)
            {
                return false;
            }

            foreach (char character in pipeName)
            {
                if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryDecodeToken(string token, out byte[] tokenBytes)
        {
            tokenBytes = null;
            int byteCount = Encoding.UTF8.GetByteCount(token);
            if (byteCount == 0 || byteCount > MaxStringBytes)
            {
                return false;
            }

            string padded = token.Replace('-', '+').Replace('_', '/');
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
                byte[] decoded = Convert.FromBase64String(padded);
                if (decoded.Length < 32)
                {
                    return false;
                }

                tokenBytes = decoded;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
