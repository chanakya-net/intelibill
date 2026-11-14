using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>A local Mailpit capture; nothing it receives leaves the container.</summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("axllent/mailpit:v1.33")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/livez")))
        .Build();

    internal string SmtpHost => _container.Hostname;
    internal int SmtpPort => _container.GetMappedPublicPort(1025);
    private Uri Api => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(8025)}/api/v1/");

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    internal Dictionary<string, string?> Settings() => new()
    {
        ["AdminMail:Host"] = SmtpHost,
        ["AdminMail:Port"] = SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["AdminMail:LocalCapture"] = "true",
        ["AdminMail:FromEmail"] = "provider-console@example.test",
        ["AdminMail:FromName"] = "InteliBill Provider Console",
    };

    /// <summary>Plain-text bodies of every captured message addressed to <paramref name="recipient"/>.</summary>
    internal async Task<string[]> ReadAsync(string recipient)
    {
        using var http = new HttpClient();
        using var search = await http.GetAsync(new Uri(Api, "search?query=" + Uri.EscapeDataString($"to:\"{recipient}\"")));
        search.EnsureSuccessStatusCode();
        using var list = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        var bodies = new List<string>();
        foreach (var message in list.RootElement.GetProperty("messages").EnumerateArray())
        {
            var detail = await http.GetFromJsonAsync<JsonElement>(new Uri(Api, "message/" + message.GetProperty("ID").GetString()));
            bodies.Add(detail.GetProperty("Text").GetString()!);
        }
        return [.. bodies];
    }
}
