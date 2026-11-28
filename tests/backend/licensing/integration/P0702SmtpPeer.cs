using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Receives one real SMTP DATA transaction. The test controls when and whether its final reply arrives.</summary>
internal sealed class P0702SmtpPeer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(50));
    private readonly TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string?> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _conversation;

    internal P0702SmtpPeer()
    {
        _listener.Start();
        _conversation = ServeAsync(_stop.Token);
    }

    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    internal Task Received => _received.Task.WaitAsync(_stop.Token);
    internal void Finish(bool acknowledge) => _reply.TrySetResult(acknowledge ? "250 accepted" : null);
    internal void Reject(int code) => _reply.TrySetResult($"{code} confidential-fixture-reply");

    private async Task ServeAsync(CancellationToken ct)
    {
        using var socket = await _listener.AcceptTcpClientAsync(ct);
        await using var stream = socket.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
        await writer.WriteLineAsync("220 fixture");
        while (await reader.ReadLineAsync(ct) is { } command)
        {
            if (!string.Equals(command, "DATA", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("250 fixture");
                continue;
            }
            await writer.WriteLineAsync("354 end with dot");
            while (await reader.ReadLineAsync(ct) is { } line && line != ".")
            {
                // Discard all bytes, including the synthetic recipient's proof.
            }
            _received.TrySetResult();
            if (await _reply.Task.WaitAsync(ct) is { } reply)
                await writer.WriteLineAsync(reply);
            return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reply.TrySetResult(null);
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _conversation;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        _stop.Dispose();
    }
}
