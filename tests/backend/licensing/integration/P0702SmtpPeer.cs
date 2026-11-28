using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Local SMTP peer with controllable greeting, DATA and acknowledgement boundaries.</summary>
internal sealed class P0702SmtpPeer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(50));
    private readonly TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string?> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _conversation;
    private readonly TaskCompletionSource _data = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _allowData = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GreetingGate? _nextGreeting;
    private int _submissions;
    private int _rejectNext;
    internal void RejectNext() => Interlocked.Exchange(ref _rejectNext, 1);
    internal int Submissions => Volatile.Read(ref _submissions);
    internal Task BeforeData => _data.Task.WaitAsync(_stop.Token);
    internal void AllowData() => _allowData.TrySetResult();

    internal sealed class GreetingGate
    {
        internal TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal GreetingGate PauseNextGreeting()
    {
        var gate = new GreetingGate();
        Assert.Null(Interlocked.Exchange(ref _nextGreeting, gate));
        return gate;
    }

    internal P0702SmtpPeer(bool pauseBeforeData = false)
    {
        if (!pauseBeforeData)
            AllowData();
        _listener.Start();
        _conversation = ServeAsync(_stop.Token);
    }

    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    internal Task Received => _received.Task.WaitAsync(_stop.Token);
    internal void Finish(bool acknowledge) => _reply.TrySetResult(acknowledge ? "250 accepted" : null);
    internal void Reject(int code) => _reply.TrySetResult($"{code} confidential-fixture-reply");

    private async Task ServeAsync(CancellationToken ct)
    {
        var conversations = new List<Task>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var socket = await _listener.AcceptTcpClientAsync(ct);
                conversations.Add(ReceiveAsync(socket, ct));
            }
        }
        finally
        {
            await Task.WhenAll(conversations);
        }
    }

    private async Task ReceiveAsync(TcpClient socket, CancellationToken ct)
    {
        using var connection = socket;
        if (Interlocked.Exchange(ref _nextGreeting, null) is { } gate)
        {
            gate.Connected.TrySetResult();
            await gate.Release.Task.WaitAsync(ct);
        }
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
            _data.TrySetResult();
            await _allowData.Task.WaitAsync(ct);
            await writer.WriteLineAsync("354 end with dot");
            while (await reader.ReadLineAsync(ct) is { } line && line != ".")
            {
                // Discard all bytes, including the synthetic recipient's proof.
            }
            Interlocked.Increment(ref _submissions);
            _received.TrySetResult();
            if (Interlocked.Exchange(ref _rejectNext, 0) == 1)
                await writer.WriteLineAsync("451 retry later");
            else if (await _reply.Task.WaitAsync(ct) is { } reply)
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
