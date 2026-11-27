using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI;

namespace HsnFinder.Api.Tests;

// Stands in for the OpenAI Responses endpoint: records the request and answers with a completed response
// whose output text is the given value. No network is involved.
internal sealed class StubResponsesHandler(string outputText = "ok") : HttpMessageHandler
{
    public Uri? RequestUri { get; private set; }

    public string? RequestBody { get; private set; }

    // OpenAIClient.GetResponsesClient and AsIChatClient(ResponsesClient) are [Experimental("OPENAI001")].
#pragma warning disable OPENAI001
    public IChatClient CreateResponsesChatClient(string model = "test-model")
        => new OpenAIClient(
                new ApiKeyCredential("test-key"),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri("https://ai.example.test/v1"),
                    Transport = new HttpClientPipelineTransport(new HttpClient(this))
                })
            .GetResponsesClient()
            .AsIChatClient(model);
#pragma warning restore OPENAI001

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        var response = new JsonObject
        {
            ["id"] = "resp_1",
            ["object"] = "response",
            ["created_at"] = 0,
            ["status"] = "completed",
            ["model"] = "test-model",
            ["output"] = new JsonArray(new JsonObject
            {
                ["type"] = "message",
                ["id"] = "msg_1",
                ["status"] = "completed",
                ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "output_text",
                    ["text"] = outputText,
                    ["annotations"] = new JsonArray()
                })
            })
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }
}
