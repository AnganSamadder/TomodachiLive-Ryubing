using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Ncm;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.HLE.FileSystem;
using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ryujinx.Ava.UI.Models
{
    public class FirmwareAvatarCache : BaseModel, IReadOnlyDictionary<string, byte[]>
    {
        private readonly Dictionary<string, byte[]> _backing = new();

        public FirmwareAvatarCache(ContentManager contentManager, VirtualFileSystem virtualFileSystem)
        {
            string contentPath = contentManager.GetInstalledContentPath(0x010000000000080A, StorageId.BuiltInSystem, NcaContentType.Data);
            string avatarPath = VirtualFileSystem.SwitchPathToSystemPath(contentPath);

            if (!string.IsNullOrWhiteSpace(avatarPath))
            {
                using IStorage ncaFileStream = new LocalStorage(avatarPath, FileAccess.Read, FileMode.Open);

                Nca nca = new(virtualFileSystem.KeySet, ncaFileStream);
                IFileSystem romfs = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);

                foreach (DirectoryEntryEx item in romfs.EnumerateEntries())
                {
                    // TODO: Parse DatabaseInfo.bin and table.bin files for more accuracy.
                    if (item.Type == DirectoryEntryType.File && item.FullPath.Contains("chara") && item.FullPath.Contains("szs"))
                    {
                        using UniqueRef<IFile> file = new();

                        romfs.OpenFile(ref file.Ref, ("/" + item.FullPath).ToU8Span(), OpenMode.Read).ThrowIfFailure();

                        using MemoryStream stream = new();
                        using MemoryStream streamPng = new();

                        file.Get.AsStream().CopyTo(stream);

                        stream.Position = 0;

                        SKImage avatarImage = SKImage.FromPixelCopy(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul), DecompressYaz0(stream));

                        using (SKData data = avatarImage.Encode(SKEncodedImageFormat.Png, 100))
                        {
                            data.SaveTo(streamPng);
                        }

                        _backing[item.FullPath] = streamPng.ToArray();
                    }
                }
            }
        }

        public IEnumerable<ProfileImageModel> CreateProfileImageModels() 
            => this.Select(x => new ProfileImageModel(x.Key, x.Value));

        private static byte[] DecompressYaz0(MemoryStream stream)
        {
            using BinaryReader reader = new(stream);

            reader.ReadInt32(); // Magic

            uint decodedLength = BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());

            reader.ReadInt64(); // Padding

            byte[] input = new byte[stream.Length - stream.Position];
            stream.ReadExactly(input, 0, input.Length);

            uint inputOffset = 0;

            byte[] output = new byte[decodedLength];
            uint outputOffset = 0;

            ushort mask = 0;
            byte header = 0;

            while (outputOffset < decodedLength)
            {
                if ((mask >>= 1) == 0)
                {
                    header = input[inputOffset++];
                    mask = 0x80;
                }

                if ((header & mask) != 0)
                {
                    if (outputOffset == output.Length)
                    {
                        break;
                    }

                    output[outputOffset++] = input[inputOffset++];
                }
                else
                {
                    byte byte1 = input[inputOffset++];
                    byte byte2 = input[inputOffset++];

                    uint dist = (uint)((byte1 & 0xF) << 8) | byte2;
                    uint position = outputOffset - (dist + 1);

                    uint length = (uint)byte1 >> 4;
                    if (length == 0)
                    {
                        length = (uint)input[inputOffset++] + 0x12;
                    }
                    else
                    {
                        length += 2;
                    }

                    uint gap = outputOffset - position;
                    uint nonOverlappingLength = length;

                    if (nonOverlappingLength > gap)
                    {
                        nonOverlappingLength = gap;
                    }

                    Buffer.BlockCopy(output, (int)position, output, (int)outputOffset, (int)nonOverlappingLength);
                    outputOffset += nonOverlappingLength;
                    position += nonOverlappingLength;
                    length -= nonOverlappingLength;

                    while (length-- > 0)
                    {
                        output[outputOffset++] = output[position++];
                    }
                }
            }

            return output;
        }

        #region dictionary impl

        IEnumerator<KeyValuePair<string, byte[]>> IEnumerable<KeyValuePair<string, byte[]>>.GetEnumerator()
        {
            return (_backing as IEnumerable<KeyValuePair<string, byte[]>>).GetEnumerator();
        }

        public IEnumerator GetEnumerator()
        {
            return ((IEnumerable)_backing).GetEnumerator();
        }

        public int Count => _backing.Count;
        public bool ContainsKey(string key) => _backing.ContainsKey(key);

        public bool TryGetValue(string key, out byte[] value) => _backing.TryGetValue(key, out value);

        public byte[] this[string key] => _backing[key];

        public IEnumerable<string> Keys => _backing.Keys;
        public IEnumerable<byte[]> Values => _backing.Values;

        #endregion
    }
}
