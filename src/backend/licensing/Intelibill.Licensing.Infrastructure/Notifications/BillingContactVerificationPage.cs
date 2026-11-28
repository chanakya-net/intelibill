using System.Net;
using System.Security.Cryptography;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Notifications;

public sealed class BillingContactVerificationPage
{
    private readonly Uri? _origin;

    internal BillingContactVerificationPage(BillingEmailOptions options, AdminMailOptions smtp, IHostEnvironment environment)
    {
        if (options.CanCapture(smtp, environment))
            _origin = BillingEmailOptions.LocalOrigin(options.VerificationOrigin);
    }

    public bool Permits(string origin) => _origin is not null
        && string.Equals(_origin.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase);

    public static (string Html, string Nonce) Render(string? language)
    {
        var locale = LicenseNoticesV1.NormalizeLocale(language);
        var text = BillingEmailRenderer.Templates[locale].GetProperty("proof");
        string Copy(string key) => WebUtility.HtmlEncode(text.GetProperty(key).GetString())!;
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(20));
        var html = $$"""
            <!doctype html><html lang="{{locale}}"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1"><title>{{Copy("subject")}}</title></head>
            <body><main><h1>{{Copy("subject")}}</h1><p>{{Copy("body")}}</p>
            <form><label>{{Copy("email")}} <input id="email" type="email" required maxlength="254"></label>
            <p><label><input id="optIn" type="checkbox"> {{Copy("optIn")}}</label></p>
            <button disabled>{{Copy("confirm")}}</button></form>
            <p role="status" id="status" data-success="{{Copy("success")}}" data-failure="{{Copy("failure")}}"></p></main>
            <script nonce="{{nonce}}">
            const fields = new URLSearchParams(location.hash.slice(1));
            history.replaceState(null, '', location.pathname + location.search);
            const form = document.querySelector('form'), button = form.querySelector('button');
            const operationKey = crypto.randomUUID();
            button.disabled = !(fields.get('account') && fields.get('contact') && fields.get('proof'));
            form.onsubmit = async event => {
              event.preventDefault();
              button.disabled = true;
              const status = document.getElementById('status');
              try {
                const response = await fetch(location.pathname, {method:'POST', credentials:'omit', cache:'no-store',
                  headers:{'Content-Type':'application/json'}, body:JSON.stringify({
                    accountId:fields.get('account'), contactId:fields.get('contact'), proofToken:fields.get('proof'),
                    verifiedEmail:document.getElementById('email').value, optedIn:document.getElementById('optIn').checked,
                    operationKey, locale:document.documentElement.lang
                  })});
                status.textContent = response.ok ? status.dataset.success : status.dataset.failure;
                if (response.ok) { fields.delete('proof'); form.hidden = true; } else button.disabled = false;
              } catch { status.textContent = status.dataset.failure; button.disabled = false; }
            };
            </script></body></html>
            """;
        return (html, nonce);
    }
}
