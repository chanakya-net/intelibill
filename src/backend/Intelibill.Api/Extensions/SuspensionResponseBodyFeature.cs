using System.Buffers;
using System.IO.Pipelines;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Shared.Web.PublicErrors;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Api.Extensions;

internal sealed class SuspensionResponseBodyFeature(
    HttpContext http, IHttpResponseBodyFeature inner, ISuspensionAccessReader reader, SessionClaims? session, Guid shop)
    : IHttpResponseBodyFeature
{
    private const int ChunkSize = 64 * 1024;
    private readonly KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>[] _initialHeaders =
        http.Response.Headers.ToArray();
    private Stream? _stream;
    private PipeWriter? _writer;
    private ArrayBufferWriter<byte>? _json;
    private LicenceAction _action = LicenceAction.History;
    public bool Stopped { get; private set; }

    public void ConfirmMemberRemoval() => _action = LicenceAction.MemberRemoval;

    public Stream Stream => _stream ??= new GuardedStream(this);
    public PipeWriter Writer => _writer ??= PipeWriter.Create(Stream, new StreamPipeWriterOptions(leaveOpen: true));
    public void DisableBuffering() => inner.DisableBuffering();

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        http.Response.HasStarted ? Task.CompletedTask : HandoffAsync(inner.StartAsync, cancellationToken);

    public async Task CompleteAsync()
    {
        await FlushJsonAsync(http.RequestAborted);
        await HandoffAsync(CompleteTransportAsync, http.RequestAborted);
    }

    public async Task FinishAsync()
    {
        await FlushJsonAsync(http.RequestAborted);
        await StartAsync(http.RequestAborted);
    }

    private async Task CompleteTransportAsync(CancellationToken ct)
    {
        // The underlying completion API has no token. Aborting lets us await it without
        // abandoning a transport operation that could still flush under an expired fence.
        using var abort = ct.UnsafeRegister(static state => ((HttpContext)state!).Abort(), http);
        await inner.CompleteAsync();
    }

    public async Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(path);
        file.Position = offset;
        var remaining = count ?? file.Length - offset;
        var buffer = new byte[ChunkSize];
        while (remaining > 0)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException();
            await WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
    }

    public ValueTask ReleaseWriterAsync() =>
        _writer?.CompleteAsync(new SuspensionResponseStoppedException()) ?? ValueTask.CompletedTask;

    private async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        if (!BuffersJson)
        {
            await WriteChunksAsync(buffer, ct);
            return;
        }

        // MVC's serializer flushes its own buffers during encoding. Coalesce those flushed bytes
        // outside the database fence, then authorize each bounded transport chunk.
        _json ??= new ArrayBufferWriter<byte>();
        while (!buffer.IsEmpty)
        {
            var chunk = buffer[..Math.Min(buffer.Length, ChunkSize - _json.WrittenCount)];
            _json.Write(chunk.Span);
            buffer = buffer[chunk.Length..];
            if (_json.WrittenCount == ChunkSize)
                await FlushJsonAsync(ct);
        }
    }

    private bool BuffersJson => http.Response.StatusCode < StatusCodes.Status400BadRequest &&
        http.Response.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true;

    private async Task FlushJsonAsync(CancellationToken ct)
    {
        if (_json is not { WrittenCount: > 0 })
            return;
        await WriteChunksAsync(_json.WrittenMemory, ct);
        _json.Clear();
    }

    private async Task WriteChunksAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        while (!buffer.IsEmpty)
        {
            var chunk = buffer[..Math.Min(buffer.Length, ChunkSize)];
            await HandoffAsync(token => inner.Stream.WriteAsync(chunk, token).AsTask(), ct);
            buffer = buffer[chunk.Length..];
        }
    }

    private async Task HandoffAsync(Func<CancellationToken, Task> send, CancellationToken ct)
    {
        if (Stopped)
            throw new SuspensionResponseStoppedException();

        // Preserve identity/permission refusals and safe infrastructure errors. Business conflicts
        // can carry shop data (for example, current balances) and use the same delivery boundary.
        if (http.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or
            StatusCodes.Status404NotFound or >= StatusCodes.Status500InternalServerError)
        {
            await send(ct);
            return;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, http.RequestAborted);
        Error? refusal;
        try
        {
            refusal = await reader.DeliverAsync(shop, session, send, lifetime.Token, _action);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            refusal = LicenceErrors.VerificationUnavailable;
        }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
        {
            // A delivery budget expired. The awaited send has stopped before the fence was released.
            Stopped = true;
            http.Abort();
            throw new SuspensionResponseStoppedException();
        }

        if (refusal is { } denied)
            await RefuseAsync(denied, lifetime.Token);
    }

    private async Task RefuseAsync(Error denied, CancellationToken ct)
    {
        Stopped = true;
        if (http.Response.HasStarted)
        {
            http.Abort();
            throw new SuspensionResponseStoppedException();
        }

        var response = http.Response;
        response.Headers.Clear();
        foreach (var (name, value) in _initialHeaders)
            response.Headers[name] = value;

        response.Headers.CacheControl = "no-store";
        response.StatusCode = denied.Code == LicenceErrors.VerificationUnavailableCode
            ? StatusCodes.Status503ServiceUnavailable
            : denied.Type switch
            {
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status403Forbidden,
            };
        response.ContentType = "application/problem+json";
        var problem = new PublicError(response.StatusCode, denied.Code, denied.Description, SafeTraceId.Resolve(http));
        await JsonSerializer.SerializeAsync(inner.Stream, problem, JsonSerializerOptions.Web, ct);
        await inner.Stream.FlushAsync(ct);
        throw new SuspensionResponseStoppedException();
    }

    private sealed class GuardedStream(SuspensionResponseBodyFeature owner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            owner.BuffersJson ? Task.CompletedTask : owner.HandoffAsync(owner.InnerStream.FlushAsync, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            owner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(owner.WriteAsync(buffer, cancellationToken));
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private Stream InnerStream => inner.Stream;
}

internal sealed class SuspensionResponseStoppedException : Exception;
