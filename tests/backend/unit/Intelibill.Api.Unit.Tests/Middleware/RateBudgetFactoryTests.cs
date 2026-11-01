using System.Net;
using System.Security.Claims;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Api.Options;
using Intelibill.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.Middleware;

public sealed class RateBudgetFactoryTests
{
    private const string CurrentKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string PreviousKey = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";
    private static readonly RateLimitPolicy AccountPolicy = new(10, 1, 3) { Subject = RateLimitSubject.Account };
    private static readonly RateLimitPolicy DestinationPolicy = new(5, 15, 15) { Subject = RateLimitSubject.Destination };

    [Fact]
    public void EmailCaseAndWhitespace_ShareKey()
    {
        var factory = CreateFactory();

        var a = factory.Create(Context(new Subject(" User@Example.COM ")), AccountPolicy).Subject;
        var b = factory.Create(Context(new Subject("user@example.com")), AccountPolicy).Subject;

        Assert.Equal(a.Select(x => (x.PolicyId, x.Key)), b.Select(x => (x.PolicyId, x.Key)));
    }

    [Fact]
    public void PhoneFormats_ShareKey()
    {
        var factory = CreateFactory();

        var a = factory.Create(Context(new Subject("+91 98765 43210")), DestinationPolicy).Subject;
        var b = factory.Create(Context(new Subject("+919876543210")), DestinationPolicy).Subject;

        Assert.Equal(a.Select(x => x.Key), b.Select(x => x.Key));
    }

    [Fact]
    public void DifferentRecordIds_ShareRouteBudget()
    {
        var factory = CreateFactory();
        var policy = new RateLimitPolicy(100, 1, 3);

        var first = factory.Create(Context(path: "/api/items/1", template: "api/items/{itemId}"), policy).Request.Single();
        var second = factory.Create(Context(path: "/api/items/2", template: "api/items/{itemId}"), policy).Request.Single();

        Assert.Equal("req:GET api/items/{itemid}", first.PolicyId);
        Assert.Equal((first.PolicyId, first.Key), (second.PolicyId, second.Key));
    }

    [Fact]
    public void Keys_ContainNoRawIdentifiers()
    {
        var factory = CreateFactory();

        var set = factory.Create(Context(new Subject("victim@example.com"), address: "203.0.113.10"), DestinationPolicy);

        Assert.All(set.All, budget =>
        {
            Assert.DoesNotContain("victim", budget.PolicyId + budget.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("203.0.113.10", budget.PolicyId + budget.Key, StringComparison.Ordinal);
            Assert.Matches("^[0-9a-f]{64}$", budget.Key);
        });
    }

    [Fact]
    public void Ipv4MappedAddress_SharesIpv4Bucket()
    {
        var factory = CreateFactory();
        var policy = new RateLimitPolicy(100, 1, 3);

        var mapped = factory.Create(Context(address: "::ffff:203.0.113.10"), policy).Request.Single();
        var plain = factory.Create(Context(address: "203.0.113.10"), policy).Request.Single();

        Assert.Equal(plain.Key, mapped.Key);
    }

    [Fact]
    public void Ipv6Addresses_InOneSlash64_ShareBucket()
    {
        var factory = CreateFactory();
        var policy = new RateLimitPolicy(100, 1, 3);

        var a = factory.Create(Context(address: "2001:db8:1:2::1"), policy).Request.Single();
        var b = factory.Create(Context(address: "2001:db8:1:2:ffff:ffff:ffff:fffe"), policy).Request.Single();
        var otherNetwork = factory.Create(Context(address: "2001:db8:1:3::1"), policy).Request.Single();

        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a.Key, otherNetwork.Key);
    }

    [Fact]
    public void AccountWithoutBody_UsesAuthenticatedUser()
    {
        var factory = CreateFactory();

        var alice = factory.Create(Context(user: "11111111-1111-1111-1111-111111111111"), AccountPolicy).Subject;
        var bob = factory.Create(Context(user: "22222222-2222-2222-2222-222222222222"), AccountPolicy).Subject;
        var anonymous = factory.Create(Context(), AccountPolicy).Subject;

        Assert.Single(alice);
        Assert.NotEqual(alice[0].Key, bob[0].Key);
        Assert.Empty(anonymous);
    }

    [Fact]
    public void MissingSubject_KeepsRequestBudget()
    {
        var factory = CreateFactory();

        var set = factory.Create(Context(new Subject(null)), DestinationPolicy);

        Assert.Single(set.Request);
        Assert.Empty(set.Subject);
    }

    [Fact]
    public void Destination_AddsWindowAndDailyBudgets()
    {
        var factory = CreateFactory();

        var subject = factory.Create(Context(new Subject("a@example.com")), DestinationPolicy).Subject;

        Assert.Collection(
            subject,
            window => Assert.Equal(("subj:destination", 3, 900), (window.PolicyId, window.Limit, window.WindowSeconds)),
            day => Assert.Equal(("subj:destination:day", 10, 86400), (day.PolicyId, day.Limit, day.WindowSeconds)));
    }

    [Fact]
    public void Account_UsesAccountBudget()
    {
        var factory = CreateFactory();

        var subject = factory.Create(Context(new Subject("a@example.com")), AccountPolicy).Subject.Single();

        Assert.Equal(("subj:account", 20, 900, 0), (subject.PolicyId, subject.Limit, subject.WindowSeconds, subject.BackoffSeconds));
    }

    [Fact]
    public void PreviousKey_DuplicatesEveryBudget()
    {
        var factory = CreateFactory(new RateLimitingOptions { BucketKey = CurrentKey, PreviousBucketKey = PreviousKey });

        var set = factory.Create(Context(new Subject("a@example.com")), AccountPolicy);

        Assert.Equal(2, set.Request.Count);
        Assert.Equal(set.Request[0].PolicyId, set.Request[1].PolicyId);
        Assert.NotEqual(set.Request[0].Key, set.Request[1].Key);
        Assert.Equal(2, set.Subject.Count);
    }

    [Fact]
    public void RelaxedPolicies_WidenSubjectBudgetsToTheDefault()
    {
        var factory = CreateFactory(new RateLimitingOptions { BucketKey = CurrentKey, Limit = 100, PeriodInMinutes = 1, RelaxEndpointPolicies = true });

        var subject = factory.Create(Context(new Subject("a@example.com")), DestinationPolicy).Subject.Single();

        Assert.Equal((100, 60), (subject.Limit, subject.WindowSeconds));
    }

    private static RateBudgetFactory CreateFactory(RateLimitingOptions? options = null)
    {
        options ??= new RateLimitingOptions { BucketKey = CurrentKey };
        var phones = Substitute.For<IPhoneAuthPolicy>();
        phones.DefaultCountryCode.Returns("+91");
        var wrapped = Microsoft.Extensions.Options.Options.Create(options);
        return new RateBudgetFactory(wrapped, new RateLimitKeyHasher(wrapped), phones);
    }

    private static ActionExecutingContext Context(
        object? argument = null,
        string path = "/api/auth/login/email",
        string template = "api/auth/login/email",
        string address = "198.51.100.7",
        string? user = null)
    {
        var httpContext = new DefaultHttpContext();
        if (user is not null)
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], authenticationType: "Test"));
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(address);
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = path;

        var descriptor = new ActionDescriptor { AttributeRouteInfo = new AttributeRouteInfo { Template = template } };
        var arguments = new Dictionary<string, object?>();
        if (argument is not null)
            arguments["request"] = argument;

        return new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), descriptor),
            filters: [],
            actionArguments: arguments,
            controller: new object());
    }

    private sealed record Subject(string? Value) : IRateLimitSubject
    {
        string? IRateLimitSubject.Subject => Value;
    }
}
