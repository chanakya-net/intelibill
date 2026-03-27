using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

public class ApiPipelineIntegrationTests
{
    [Fact]
    public async Task OpenApi_InDevelopment_IsAvailable()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ShopsMe_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/shops/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterAndLogin_Flow_ReturnsAuthPayload()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        var email = $"integration-{Guid.NewGuid():N}@test.com";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email,
            password = "Pass123!",
            firstName = "Integration",
            lastName = "User"
        });

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registerPayload = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = registerPayload.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email,
            password = "Pass123!"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var loginAccessToken = loginPayload.GetProperty("accessToken").GetString();
        var refreshToken = loginPayload.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(loginAccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));
    }
}
