using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// Posts one message to the Twilio Messages API. A form-encoded request over a named
/// <see cref="HttpClient"/> rather than the vendor SDK: the call is three fields, and a transport
/// package would be a dependency to track for no added behaviour.
/// <para>
/// <c>DltEntityId</c> and <c>DltTemplateId</c> are sent only when configured. Without them Twilio
/// routes into India over the international gateway, which needs no TRAI DLT registration but
/// replaces the sender id with a random short code; with them it uses the domestic gateway, which
/// requires the registered entity, header and template.
/// </para>
/// </summary>
internal sealed class TwilioSmsSender(IHttpClientFactory clients, IOptions<PhoneAuthOptions> options) : ISmsSender
{
    internal const string HttpClientName = "TwilioSms";

    public async Task SendIdentityAsync(string destination, string body, CancellationToken ct)
    {
        var settings = options.Value;
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", destination),
            new("Body", body),
        };

        if (!string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
            fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", settings.MessagingServiceSid));
        else
            fields.Add(new KeyValuePair<string, string>("From", settings.FromNumber));

        if (!string.IsNullOrWhiteSpace(settings.DltEntityId))
        {
            fields.Add(new KeyValuePair<string, string>("Entityid", settings.DltEntityId));
            fields.Add(new KeyValuePair<string, string>("Templateid", settings.DltTemplateId));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            string.Create(CultureInfo.InvariantCulture, $"https://api.twilio.com/2010-04-01/Accounts/{settings.AccountSid}/Messages.json"))
        {
            Content = new FormUrlEncodedContent(fields),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));

        using var client = clients.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            // The status alone. A provider error body echoes the message, and the message is the
            // code.
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"The SMS provider rejected the request with status {(int)response.StatusCode}."),
                inner: null,
                response.StatusCode);
        }
    }
}
