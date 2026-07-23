using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthSessionValidationTests
{
    [Fact]
    public async Task AccessToken_WhenPermissionVersionChanged_ShouldReturnUnauthorized()
    {
        using var factory = new RealJwtApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (accessToken, _) = await LoginAsAdminAsync(client);

        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/system-users?page=1&pageSize=20"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await dbContext.SystemUsers.FirstAsync(user => user.Username == "admin");
            admin.PermissionVersion += 1;
            await dbContext.SaveChangesAsync();
        }

        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/system-users?page=1&pageSize=20"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task RefreshToken_WhenUsedAsAccessToken_ShouldReturnUnauthorized()
    {
        using var factory = new RealJwtApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (_, refreshToken) = await LoginAsAdminAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/system-users?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshToken);

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<(string AccessToken, string RefreshToken)> LoginAsAdminAsync(HttpClient client)
    {
        using var loginRequest = AuthCookieTestHelper.CreateLoginRequest(
            "admin", ApiWebApplicationFactory.TestAdminPassword);
        var response = await client.SendAsync(loginRequest);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.ReadAsAsync<JsonElement>();
        var data = body.GetProperty("data");
        var (refreshToken, _) = AuthCookieTestHelper.ReadSessionCookies(response);
        return (
            data.GetProperty("accessToken").GetString()!,
            refreshToken);
    }
}
