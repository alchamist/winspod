using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MudServer
{
    /// <summary>
    /// A pass-through Stream decorator that feeds Server.BytesIn/BytesOut for the
    /// `netstat` admin command - Connection wraps its incoming Stream in one of these
    /// (see its constructor) so every byte crossing the wire gets counted at this single
    /// chokepoint, rather than needing to instrument every Writer.Write/ReadStream.ReadAsync
    /// call site individually. "Packets" here means read/write calls, not literal network
    /// packets - a Stream has no visibility below the byte-stream abstraction, so this is
    /// the closest analogue available (see cmdNetstat's help text).
    /// </summary>
    public class CountingStream : Stream
    {
        private readonly Stream inner;

        public CountingStream(Stream inner)
        {
            this.inner = inner;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            if (read > 0)
            {
                Interlocked.Add(ref Server.bytesIn, read);
                Interlocked.Increment(ref Server.packetsIn);
            }
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int read = await inner.ReadAsync(buffer, offset, count, cancellationToken);
            if (read > 0)
            {
                Interlocked.Add(ref Server.bytesIn, read);
                Interlocked.Increment(ref Server.packetsIn);
            }
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            if (count > 0)
            {
                Interlocked.Add(ref Server.bytesOut, count);
                Interlocked.Increment(ref Server.packetsOut);
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(buffer, offset, count, cancellationToken);
            if (count > 0)
            {
                Interlocked.Add(ref Server.bytesOut, count);
                Interlocked.Increment(ref Server.packetsOut);
            }
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
