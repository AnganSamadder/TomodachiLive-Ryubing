using NUnit.Framework;
using Ryujinx.Input.Tomodachi.Ipc;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Input.Tests.Tomodachi
{
    [TestFixture]
    public class TomodachiPipeProtocolTests
    {
        [Test]
        public void HelloRejectsNonStringClientInstanceId()
        {
            byte[] payload = Encoding.UTF8.GetBytes(
                "{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"hello\",\"requestId\":\"hello\",\"clientInstanceId\":{\"nested\":\"value\"},\"token\":\"token\",\"sentAt\":\"2026-01-01T00:00:00Z\"}");

            TomodachiPipeProtocolException exception = Assert.Throws<TomodachiPipeProtocolException>(
                () => TomodachiPipeProtocol.ParseRequest(payload));

            Assert.That(exception.Code, Is.EqualTo("invalid-request"));
        }

        [Test]
        public async Task FrameReaderRejectsTruncatedPrefixZeroOversizeAndTruncatedBody()
        {
            static MemoryStream Stream(params byte[] bytes) => new(bytes);

            TomodachiPipeProtocolException prefix = Assert.ThrowsAsync<TomodachiPipeProtocolException>(
                async () => await TomodachiPipeProtocol.ReadFrameAsync(Stream(0, 0), CancellationToken.None));
            TomodachiPipeProtocolException zero = Assert.ThrowsAsync<TomodachiPipeProtocolException>(
                async () => await TomodachiPipeProtocol.ReadFrameAsync(Stream(0, 0, 0, 0), CancellationToken.None));
            TomodachiPipeProtocolException oversize = Assert.ThrowsAsync<TomodachiPipeProtocolException>(
                async () => await TomodachiPipeProtocol.ReadFrameAsync(Stream(0, 1, 0, 1), CancellationToken.None));
            TomodachiPipeProtocolException body = Assert.ThrowsAsync<TomodachiPipeProtocolException>(
                async () => await TomodachiPipeProtocol.ReadFrameAsync(Stream(0, 0, 0, 2, (byte)'{'), CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(prefix.Code, Is.EqualTo("truncated-frame"));
                Assert.That(zero.Code, Is.EqualTo("invalid-frame-length"));
                Assert.That(oversize.Code, Is.EqualTo("frame-too-large"));
                Assert.That(body.Code, Is.EqualTo("truncated-frame"));
            });
        }

        [Test]
        public async Task MaximumFrameRoundTripsWithBigEndianPrefix()
        {
            byte[] payload = new byte[TomodachiPipeProtocol.MaxFrameBytes];
            payload[0] = (byte)'{';
            payload[^1] = (byte)'}';
            using MemoryStream stream = new();

            await TomodachiPipeProtocol.WriteFrameAsync(stream, payload, CancellationToken.None);
            byte[] framed = stream.ToArray();
            stream.Position = 0;
            byte[] decoded = await TomodachiPipeProtocol.ReadFrameAsync(stream, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(BinaryPrimitives.ReadUInt32BigEndian(framed.AsSpan(0, 4)), Is.EqualTo(TomodachiPipeProtocol.MaxFrameBytes));
                Assert.That(decoded, Is.EqualTo(payload));
            });
        }

        [Test]
        public void ParserRejectsDuplicateUnknownInvalidUtf8TrailingJsonAndLongUtf8Strings()
        {
            static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);
            string common = "\"protocol\":\"tomodachi-ipc/1\",\"type\":\"health\",\"requestId\":\"r\",\"traceId\":\"t\",\"sentAt\":\"2026-01-01T00:00:00Z\"";

            TomodachiPipeProtocolException duplicate = Assert.Throws<TomodachiPipeProtocolException>(
                () => TomodachiPipeProtocol.ParseRequest(Json($"{{{common},\"requestId\":\"other\"}}")));
            TomodachiPipeProtocolException unknown = Assert.Throws<TomodachiPipeProtocolException>(
                () => TomodachiPipeProtocol.ParseRequest(Json($"{{{common},\"surprise\":true}}")));
            TomodachiPipeProtocolException invalidUtf8 = Assert.Throws<TomodachiPipeProtocolException>(
                () => TomodachiPipeProtocol.ParseRequest([0x7b, 0x22, 0x78, 0x22, 0x3a, 0xff, 0x7d]));
            TomodachiPipeProtocolException trailing = Assert.Throws<TomodachiPipeProtocolException>(
                () => TomodachiPipeProtocol.ParseRequest(Json($"{{{common}}}{{}}")));
            TomodachiPipeProtocolException tooLong = Assert.Throws<TomodachiPipeProtocolException>(
                () => TomodachiPipeProtocol.ParseRequest(Json($"{{{common.Replace("\"r\"", $"\"{new string('é', 257)}\"")}}}")));

            Assert.Multiple(() =>
            {
                Assert.That(duplicate.Code, Is.EqualTo("duplicate-property"));
                Assert.That(unknown.Code, Is.EqualTo("unknown-property"));
                Assert.That(invalidUtf8.Code, Is.EqualTo("invalid-json"));
                Assert.That(trailing.Code, Is.EqualTo("invalid-json"));
                Assert.That(tooLong.Code, Is.EqualTo("string-too-long"));
            });
        }

        [Test]
        public void ParserRejectsNestedDuplicateAndUnknownAuthorityProperties()
        {
            const string prefix = "{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"arm\",\"requestId\":\"r\",\"traceId\":\"t\",\"sentAt\":\"2026-01-01T00:00:00Z\",\"armId\":\"arm\",\"authority\":";
            TomodachiPipeProtocolException duplicate = Assert.Throws<TomodachiPipeProtocolException>(() =>
                TomodachiPipeProtocol.ParseRequest(Encoding.UTF8.GetBytes(prefix + "{\"serverInstanceId\":\"s\",\"serverInstanceId\":\"s2\",\"roomId\":\"r\",\"controlLeaseId\":\"l\",\"sessionId\":\"x\"}}")));
            TomodachiPipeProtocolException unknown = Assert.Throws<TomodachiPipeProtocolException>(() =>
                TomodachiPipeProtocol.ParseRequest(Encoding.UTF8.GetBytes(prefix + "{\"serverInstanceId\":\"s\",\"roomId\":\"r\",\"controlLeaseId\":\"l\",\"sessionId\":\"x\",\"extra\":\"no\"}}")));

            Assert.Multiple(() =>
            {
                Assert.That(duplicate.Code, Is.EqualTo("duplicate-property"));
                Assert.That(unknown.Code, Is.EqualTo("unknown-property"));
            });
        }

        [Test]
        public void ParserRejectsInvalidTimestampBeforeDispatch()
        {
            byte[] invalidTimestamp = Encoding.UTF8.GetBytes("{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"health\",\"requestId\":\"r\",\"traceId\":\"t\",\"sentAt\":\"not-a-time\"}");

            TomodachiPipeProtocolException exception = Assert.Throws<TomodachiPipeProtocolException>(() =>
                TomodachiPipeProtocol.ParseRequest(invalidTimestamp));

            Assert.That(exception.Code, Is.EqualTo("invalid-timestamp"));
        }

        [Test]
        public void ParserRejectsMissingOperationSpecificFieldsBeforeDispatch()
        {
            byte[] missingArmId = Encoding.UTF8.GetBytes("{\"protocol\":\"tomodachi-ipc/1\",\"type\":\"arm\",\"requestId\":\"r\",\"traceId\":\"t\",\"sentAt\":\"2026-01-01T00:00:00Z\",\"authority\":{\"serverInstanceId\":\"s\",\"roomId\":\"r\",\"controlLeaseId\":\"l\",\"sessionId\":\"x\"}}");

            TomodachiPipeProtocolException exception = Assert.Throws<TomodachiPipeProtocolException>(() =>
                TomodachiPipeProtocol.ParseRequest(missingArmId));

            Assert.That(exception.Code, Is.EqualTo("invalid-request"));
        }
    }
}
