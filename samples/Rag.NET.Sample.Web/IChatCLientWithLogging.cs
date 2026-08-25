using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Rag.NET.Sample.Web;

public sealed class IChatCLientWithLogging(IChatClient inner) : IChatClient
{
    private readonly IChatClient _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        LogMessages("Request", messageList);

        var response = await _inner.GetResponseAsync(messageList, options, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"[Chat:Response] {response.Text}");

        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        LogMessages("StreamingRequest", messageList);

        await foreach (var update in _inner.GetStreamingResponseAsync(messageList, options, cancellationToken).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                Console.WriteLine($"[Chat:StreamingResponse] {update.Text}");
            }

            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return _inner.GetService(serviceType, serviceKey);
    }

    private static void LogMessages(string scope, IReadOnlyList<ChatMessage> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            Console.WriteLine($">>>[Chat:{scope}] {i}: {message.Role} => {message.Text}");
        }
    }

    public void Dispose()
    {
        // no-op: we don't own the inner client, so we don't dispose it
    }
}

public static class ChatClientLoggingExtensions
{
    public static IChatClient AsLoggingChatClient(this IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        return new IChatCLientWithLogging(chatClient);
    }
}