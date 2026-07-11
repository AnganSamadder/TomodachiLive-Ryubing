using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Input.Tomodachi.Ipc
{
    public sealed class TomodachiPipeProtocolException : Exception
    {
        public string Code { get; }

        public TomodachiPipeProtocolException(string code)
            : base(code)
        {
            Code = code;
        }

        public TomodachiPipeProtocolException(string code, Exception innerException)
            : base(code, innerException)
        {
            Code = code;
        }
    }

    public static class TomodachiPipeProtocol
    {
        public const string Version = "tomodachi-ipc/1";
        public const int MaxFrameBytes = 65_536;
        public const int MaxStringBytes = 512;

        private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedRequestProperties =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["hello"] = ["protocol", "type", "requestId", "clientInstanceId", "token", "sentAt"],
                ["arm"] = ["protocol", "type", "requestId", "traceId", "sentAt", "armId", "authority"],
                ["heartbeat"] = ["protocol", "type", "requestId", "traceId", "sentAt", "authority"],
                ["input"] = ["protocol", "type", "requestId", "traceId", "sentAt", "commandId", "authority", "sequence", "expiresAt", "button", "action"],
                ["receipt.get"] = ["protocol", "type", "requestId", "traceId", "sentAt", "commandId"],
                ["health"] = ["protocol", "type", "requestId", "traceId", "sentAt"],
                ["neutralize"] = ["protocol", "type", "requestId", "traceId", "sentAt", "stopId", "reason"],
            };

        private static readonly HashSet<string> AuthorityProperties =
            ["serverInstanceId", "roomId", "controlLeaseId", "sessionId"];

        public static async ValueTask<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);

            byte[] prefix = new byte[sizeof(uint)];
            await ReadExactlyAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(prefix);
            if (length == 0)
            {
                throw new TomodachiPipeProtocolException("invalid-frame-length");
            }

            if (length > MaxFrameBytes)
            {
                throw new TomodachiPipeProtocolException("frame-too-large");
            }

            byte[] payload = new byte[length];
            await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
            return payload;
        }

        public static async ValueTask WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (payload.Length == 0)
            {
                throw new TomodachiPipeProtocolException("invalid-frame-length");
            }

            if (payload.Length > MaxFrameBytes)
            {
                throw new TomodachiPipeProtocolException("frame-too-large");
            }

            byte[] prefix = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)payload.Length);
            await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static JsonDocument ParseRequest(ReadOnlySpan<byte> payload)
        {
            if (payload.IsEmpty || payload.Length > MaxFrameBytes)
            {
                throw new TomodachiPipeProtocolException(payload.IsEmpty ? "invalid-frame-length" : "frame-too-large");
            }

            ValidateJsonTokens(payload);

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                });
            }
            catch (JsonException exception)
            {
                throw new TomodachiPipeProtocolException("invalid-json", exception);
            }

            try
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new TomodachiPipeProtocolException("invalid-request");
                }

                string protocol = GetRequiredString(root, "protocol");
                string type = GetRequiredString(root, "type");
                if (!string.Equals(protocol, Version, StringComparison.Ordinal))
                {
                    throw new TomodachiPipeProtocolException("version-mismatch");
                }

                if (!AllowedRequestProperties.TryGetValue(type, out HashSet<string> allowed))
                {
                    throw new TomodachiPipeProtocolException("unknown-request-type");
                }

                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (!allowed.Contains(property.Name))
                    {
                        throw new TomodachiPipeProtocolException("unknown-property");
                    }
                }

                foreach (string requiredProperty in allowed)
                {
                    if (!root.TryGetProperty(requiredProperty, out _))
                    {
                        throw new TomodachiPipeProtocolException("invalid-request");
                    }
                }

                GetRequiredString(root, "requestId");
                GetRequiredTimestamp(root, "sentAt");
                if (string.Equals(type, "hello", StringComparison.Ordinal))
                {
                    GetRequiredString(root, "clientInstanceId");
                    GetRequiredString(root, "token");
                }
                else
                {
                    GetRequiredString(root, "traceId");
                }

                if (root.TryGetProperty("authority", out JsonElement authority))
                {
                    ValidateAuthority(authority);
                }

                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }

        private static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < destination.Length)
            {
                int read = await stream.ReadAsync(destination[offset..], cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new TomodachiPipeProtocolException("truncated-frame");
                }

                offset += read;
            }
        }

        private static void ValidateJsonTokens(ReadOnlySpan<byte> payload)
        {
            try
            {
                Utf8JsonReader reader = new(payload, new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                });
                Stack<HashSet<string>> objectScopes = new();
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            objectScopes.Push(new HashSet<string>(StringComparer.Ordinal));
                            break;
                        case JsonTokenType.EndObject:
                            if (objectScopes.Count == 0)
                            {
                                throw new TomodachiPipeProtocolException("invalid-json");
                            }

                            objectScopes.Pop();
                            break;
                        case JsonTokenType.PropertyName:
                            string propertyName = reader.GetString();
                            ValidateString(propertyName);
                            if (objectScopes.Count == 0 || !objectScopes.Peek().Add(propertyName))
                            {
                                throw new TomodachiPipeProtocolException("duplicate-property");
                            }
                            break;
                        case JsonTokenType.String:
                            ValidateString(reader.GetString());
                            break;
                    }
                }

                if (objectScopes.Count != 0)
                {
                    throw new TomodachiPipeProtocolException("invalid-json");
                }
            }
            catch (JsonException exception)
            {
                throw new TomodachiPipeProtocolException("invalid-json", exception);
            }
            catch (DecoderFallbackException exception)
            {
                throw new TomodachiPipeProtocolException("invalid-json", exception);
            }
        }

        private static void ValidateAuthority(JsonElement authority)
        {
            if (authority.ValueKind != JsonValueKind.Object)
            {
                throw new TomodachiPipeProtocolException("invalid-authority");
            }

            int count = 0;
            foreach (JsonProperty property in authority.EnumerateObject())
            {
                if (!AuthorityProperties.Contains(property.Name))
                {
                    throw new TomodachiPipeProtocolException("unknown-property");
                }

                GetRequiredString(authority, property.Name);
                count++;
            }

            if (count != AuthorityProperties.Count)
            {
                throw new TomodachiPipeProtocolException("invalid-authority");
            }
        }

        private static string GetRequiredString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            {
                throw new TomodachiPipeProtocolException("invalid-request");
            }

            string value = property.GetString();
            ValidateString(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new TomodachiPipeProtocolException("invalid-request");
            }

            return value;
        }

        private static DateTimeOffset GetRequiredTimestamp(JsonElement root, string propertyName)
        {
            string value = GetRequiredString(root, propertyName);
            if (!DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset timestamp))
            {
                throw new TomodachiPipeProtocolException("invalid-timestamp");
            }

            return timestamp;
        }

        private static void ValidateString(string value)
        {
            if (value is null || Encoding.UTF8.GetByteCount(value) > MaxStringBytes)
            {
                throw new TomodachiPipeProtocolException("string-too-long");
            }
        }
    }
}
