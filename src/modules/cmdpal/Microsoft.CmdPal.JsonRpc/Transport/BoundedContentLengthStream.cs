// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.CmdPal.JsonRpc;

internal sealed partial class BoundedContentLengthStream : Stream
{
    private const int MaxHeaderLength = 32 * 1024;

    private readonly Stream _inner;
    private readonly int _maxContentLength;
    private readonly List<byte> _header = new();
    private int _remainingBodyBytes;

    internal BoundedContentLengthStream(Stream inner, int maxContentLength)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxContentLength);

        _inner = inner;
        _maxContentLength = maxContentLength;
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Inspect(buffer.AsSpan(offset, read));
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Inspect(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Inspect(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (_remainingBodyBytes > 0)
            {
                _remainingBodyBytes--;
                continue;
            }

            _header.Add(value);
            if (_header.Count > MaxHeaderLength)
            {
                throw new InvalidDataException($"JSON-RPC header exceeded {MaxHeaderLength.ToString(CultureInfo.InvariantCulture)} bytes.");
            }

            if (EndsWithHeaderTerminator())
            {
                var contentLength = TryParseContentLength(Encoding.ASCII.GetString(_header.ToArray()));
                _header.Clear();

                if (contentLength is null)
                {
                    continue;
                }

                if (contentLength.Value > _maxContentLength)
                {
                    throw new InvalidDataException($"JSON-RPC message body exceeded {_maxContentLength.ToString(CultureInfo.InvariantCulture)} bytes.");
                }

                _remainingBodyBytes = contentLength.Value;
            }
        }
    }

    private bool EndsWithHeaderTerminator()
    {
        var count = _header.Count;
        return count >= 4 &&
            _header[count - 4] == (byte)'\r' &&
            _header[count - 3] == (byte)'\n' &&
            _header[count - 2] == (byte)'\r' &&
            _header[count - 1] == (byte)'\n';
    }

    private static int? TryParseContentLength(string header)
    {
        foreach (var line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            if (!line.AsSpan(0, separator).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return int.TryParse(line.AsSpan(separator + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                ? length
                : null;
        }

        return null;
    }
}
