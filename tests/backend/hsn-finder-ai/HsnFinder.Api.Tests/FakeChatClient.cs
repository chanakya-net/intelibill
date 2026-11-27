using Microsoft.Extensions.AI;

namespace HsnFinder.Api.Tests;

internal sealed class FakeChatClient(
    Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> respond) : IChatClient
{
    private int _callCount;

    public int CallCount => _callCount;

    public ChatOptions? LastOptions { get; private set; }

    public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

    public static FakeChatClient Returning(string text)
        => new((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))));

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        var materialized = messages.ToList();
        LastOptions = options;
        LastMessages = materialized;
        return respond(materialized, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
