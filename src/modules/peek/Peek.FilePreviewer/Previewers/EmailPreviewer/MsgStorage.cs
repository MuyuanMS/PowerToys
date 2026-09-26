// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Peek.FilePreviewer.Previewers.EmailPreviewer
{
    internal sealed class MsgStorage : IDisposable
    {
        private const int MessagePropertyHeaderSize = 32;
        private const int ChildPropertyHeaderSize = 8;
        private MsgStorageInterop.IStorage? _storage;
        private readonly int _propertyHeaderSize;
        private Encoding _ansiEncoding;

        private MsgStorage(MsgStorageInterop.IStorage storage, Encoding? ansiEncoding = null, int propertyHeaderSize = MessagePropertyHeaderSize)
        {
            _storage = storage;
            _ansiEncoding = ansiEncoding ?? Encoding.Latin1;
            _propertyHeaderSize = propertyHeaderSize;
        }

        public static MsgStorage Open(string path)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            int result = MsgStorageInterop.StgOpenStorage(path, null, MsgStorageInterop.ReadMode, IntPtr.Zero, 0, out MsgStorageInterop.IStorage storage);
            Marshal.ThrowExceptionForHR(result);
            MsgStorage message = new(storage);
            int codePage = message.ReadInt32("3FFD");
            if (codePage <= 0)
            {
                codePage = message.ReadInt32("3FDE");
            }

            if (codePage > 0)
            {
                try
                {
                    message._ansiEncoding = Encoding.GetEncoding(codePage);
                }
                catch (ArgumentException)
                {
                }
            }

            return message;
        }

        public MsgStorage OpenStorage(string name)
        {
            GetStorage().OpenStorage(name, null, MsgStorageInterop.ReadMode, IntPtr.Zero, 0, out MsgStorageInterop.IStorage storage);
            return new MsgStorage(storage, _ansiEncoding, ChildPropertyHeaderSize);
        }

        public IEnumerable<string> EnumerateStorages(string prefix)
        {
            GetStorage().EnumElements(0, IntPtr.Zero, 0, out MsgStorageInterop.IEnumSTATSTG enumerator);
            try
            {
                STATSTG[] item = new STATSTG[1];
                while (enumerator.Next(1, item, IntPtr.Zero) == 0)
                {
                    if (item[0].type == MsgStorageInterop.StorageType && item[0].pwcsName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return item[0].pwcsName;
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(enumerator);
            }
        }

        public int ReadInt32(string propertyId)
        {
            byte[]? bytes = ReadStream($"__substg1.0_{propertyId}0003");
            if (bytes?.Length >= sizeof(int))
            {
                return BitConverter.ToInt32(bytes, 0);
            }

            byte[]? properties = ReadStream("__properties_version1.0");
            if (properties == null)
            {
                return 0;
            }

            uint tag = ((uint)ushort.Parse(propertyId, NumberStyles.HexNumber, CultureInfo.InvariantCulture) << 16) | 0x0003;
            for (int offset = _propertyHeaderSize; offset + 16 <= properties.Length; offset += 16)
            {
                if (BitConverter.ToUInt32(properties, offset) == tag)
                {
                    return BitConverter.ToInt32(properties, offset + 8);
                }
            }

            return 0;
        }

        public Encoding GetHtmlEncoding()
        {
            int codePage = ReadInt32("3FDE");
            if (codePage <= 0)
            {
                codePage = ReadInt32("3FFD");
            }

            if (codePage > 0)
            {
                try
                {
                    return Encoding.GetEncoding(codePage);
                }
                catch (ArgumentException)
                {
                }
            }

            return Encoding.UTF8;
        }

        public string ReadString(string propertyId)
        {
            byte[]? unicode = ReadStream($"__substg1.0_{propertyId}001F");
            if (unicode != null)
            {
                return Encoding.Unicode.GetString(unicode).TrimEnd('\0');
            }

            byte[]? ansi = ReadStream($"__substg1.0_{propertyId}001E");
            return ansi == null ? string.Empty : _ansiEncoding.GetString(ansi).TrimEnd('\0');
        }

        public byte[]? ReadStream(string name)
        {
            try
            {
                GetStorage().OpenStream(name, IntPtr.Zero, MsgStorageInterop.ReadMode, 0, out IStream stream);
                try
                {
                    stream.Stat(out STATSTG stat, 1);
                    if (stat.cbSize < 0 || stat.cbSize > int.MaxValue)
                    {
                        throw new InvalidDataException("The MSG property is too large to preview.");
                    }

                    byte[] buffer = new byte[(int)stat.cbSize];
                    IntPtr readPointer = Marshal.AllocCoTaskMem(sizeof(int));
                    try
                    {
                        stream.Read(buffer, buffer.Length, readPointer);
                        int bytesRead = Marshal.ReadInt32(readPointer);
                        return bytesRead == buffer.Length ? buffer : buffer[..bytesRead];
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(readPointer);
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(stream);
                }
            }
            catch (COMException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_storage != null)
            {
                Marshal.FinalReleaseComObject(_storage);
                _storage = null;
            }

            GC.SuppressFinalize(this);
        }

        private MsgStorageInterop.IStorage GetStorage() => _storage ?? throw new ObjectDisposedException(nameof(MsgStorage));
    }
}
