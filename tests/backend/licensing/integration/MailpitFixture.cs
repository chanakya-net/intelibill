using Intelibill.Licensing.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
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

    internal Dictionary<string, string?> BillingSettings(bool runInBackground = false)
    {
        var settings = Settings();
        settings["BillingEmail:Enabled"] = "true";
        settings["BillingEmail:RunInBackground"] = runInBackground.ToString();
        settings["BillingEmail:FromEmail"] = "billing@example.test";
        settings["BillingEmail:SupportEmail"] = "billing-support@example.test";
        settings["BillingEmail:MerchantOrigin"] = "http://localhost:4200";
        settings["BillingEmail:VerificationOrigin"] = "http://localhost:5081";
        settings["BillingEmail:MailpitUiOrigin"] = Api.GetLeftPart(UriPartial.Authority);
        return settings;
    }

    internal async Task<string> ReadProofAsync(IServiceProvider services, string address, Guid contact)
    {
        await using var scope = services.CreateAsyncScope();
        var pump = scope.ServiceProvider.GetRequiredService<IBillingEmailDispatcher>();
        for (var i = 0; i < 50 && await pump.DeliverOneAsync(CancellationToken.None); i++)
        {
            var bodies = await ReadAsync(address);
            foreach (var line in bodies.SelectMany(body => body.Split('\n')))
            {
                if (!Uri.TryCreate(line.Trim(), UriKind.Absolute, out var uri) || uri.AbsolutePath != "/billing-contact/verify")
                    continue;
                var fields = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Fragment.TrimStart('#'));
                if (fields["contact"] == contact.ToString("D"))
                    return fields["proof"].ToString();
            }
        }
        throw new InvalidOperationException("No verification email captured for the approved contact.");
    }

    internal async Task<string[]> ReadAsync(string recipient) =>
        (await MessagesAsync(recipient)).Select(message => message.GetProperty("Text").GetString()!).ToArray();

    internal async Task<JsonElement[]> MessagesAsync(string recipient)
    {
        using var http = new HttpClient();
        using var search = await http.GetAsync(new Uri(Api, "search?query=" + Uri.EscapeDataString($"to:\"{recipient}\"")));
        search.EnsureSuccessStatusCode();
        using var list = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        var captures = new List<JsonElement>();
        foreach (var message in list.RootElement.GetProperty("messages").EnumerateArray())
        {
            var detail = await http.GetFromJsonAsync<JsonElement>(new Uri(Api, "message/" + message.GetProperty("ID").GetString()));
            captures.Add(detail);
        }
        return [.. captures];
    }
}
