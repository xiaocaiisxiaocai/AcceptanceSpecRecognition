using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AcceptanceSpecSystem.Api.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthCookieLifecycleTests
{
    [Fact]
    public async Task RefreshToken_ShouldRotateAndReplayShouldRevokeFamily()
    {
        await using var factory = new RealJwtApiWebApplicationFactory();
        using var client = CreateClient(factory);
        var configuredOrigins = factory.Services.GetRequiredService<IOptions<BrowserAuthOptions>>()
            .Value.AllowedOrigins;
        configuredOrigins.Should().Contain(AuthCookieTestHelper.AllowedOrigin);
        using var login = await LoginAsync(client);
        var (originalRefresh, originalCsrf) = AuthCookieTestHelper.ReadSessionCookies(login);

        using var firstRequest = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", originalRefresh, originalCsrf);
        using var first = await client.SendAsync(firstRequest);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var (rotatedRefresh, rotatedCsrf) = AuthCookieTestHelper.ReadSessionCookies(first);

        using var replayRequest = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", originalRefresh, originalCsrf);
        using var replay = await client.SendAsync(replayRequest);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var familyRequest = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", rotatedRefresh, rotatedCsrf);
        using var family = await client.SendAsync(familyRequest);
        family.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "旧令牌重放后整个会话族都应失效");
    }

    [Fact]
    public async Task ConcurrentRefresh_ShouldAllowAtMostOneRotationAndDetectReplay()
    {
        await using var factory = new RealJwtApiWebApplicationFactory();
        using var loginClient = CreateClient(factory);
        using var login = await LoginAsync(loginClient);
        var (refreshToken, csrfToken) = AuthCookieTestHelper.ReadSessionCookies(login);
        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);
        using var firstRequest = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", refreshToken, csrfToken);
        using var secondRequest = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", refreshToken, csrfToken);

        var responses = await Task.WhenAll(firstClient.SendAsync(firstRequest), secondClient.SendAsync(secondRequest));
        try
        {
            var diagnosticItems = await Task.WhenAll(responses.Select(async response =>
                $"{(int)response.StatusCode}:{await response.Content.ReadAsStringAsync()}"));
            var diagnostics = string.Join(",", diagnosticItems);
            if (responses.Count(response => response.StatusCode == HttpStatusCode.OK) != 1 ||
                responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized) != 1)
                throw new Xunit.Sdk.XunitException(diagnostics);
            responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
            responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized).Should().Be(1);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task RefreshToken_WithoutCsrfOrTrustedOrigin_ShouldRejectWithoutConsumingToken()
    {
        await using var factory = new RealJwtApiWebApplicationFactory();
        using var client = CreateClient(factory);
        using var login = await LoginAsync(client);
        var (refreshToken, csrfToken) = AuthCookieTestHelper.ReadSessionCookies(login);

        using (var missingCsrf = new HttpRequestMessage(HttpMethod.Post, "/refresh-token")
        {
            Content = ApiClientJson.ToJsonContent(new { })
        })
        {
            missingCsrf.Headers.Add("Origin", AuthCookieTestHelper.AllowedOrigin);
            missingCsrf.Headers.Add("Cookie", $"{AuthCookieTestHelper.RefreshCookieName}={refreshToken}; {AuthCookieTestHelper.CsrfCookieName}={csrfToken}");
            using var response = await client.SendAsync(missingCsrf);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var badOrigin = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", refreshToken, csrfToken, "https://evil.example"))
        using (var response = await client.SendAsync(badOrigin))
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var valid = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", refreshToken, csrfToken);
        using var validResponse = await client.SendAsync(valid);
        validResponse.StatusCode.Should().Be(HttpStatusCode.OK, "CSRF 拒绝不得消耗一次性 RefreshToken");
    }

    [Fact]
    public async Task Logout_ShouldRevokeServerSessionAndClearCookies()
    {
        await using var factory = new RealJwtApiWebApplicationFactory();
        using var client = CreateClient(factory);
        using var login = await LoginAsync(client);
        var (refreshToken, csrfToken) = AuthCookieTestHelper.ReadSessionCookies(login);

        using var logoutRequest = AuthCookieTestHelper.CreateStateChangingRequest("/logout", refreshToken, csrfToken);
        using var logout = await client.SendAsync(logoutRequest);
        logout.StatusCode.Should().Be(HttpStatusCode.OK);
        logout.Headers.GetValues("Set-Cookie").Should().Contain(value =>
            value.StartsWith(AuthCookieTestHelper.RefreshCookieName + "=") &&
            value.Contains("expires=thu, 01 jan 1970", StringComparison.OrdinalIgnoreCase));

        using var refreshRequest = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", refreshToken, csrfToken);
        using var refresh = await client.SendAsync(refreshRequest);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ShouldPersistOnlyRefreshTokenHash()
    {
        await using var factory = new RealJwtApiWebApplicationFactory();
        using var client = CreateClient(factory);
        using var login = await LoginAsync(client);
        var (refreshToken, _) = AuthCookieTestHelper.ReadSessionCookies(login);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AuthRefreshSessions.SingleAsync();
        session.TokenHash.Should().NotBe(refreshToken);
        session.TokenHash.Should().MatchRegex("^[0-9A-F]{64}$");
        session.Status.Should().Be(AuthRefreshSessionStatus.Active);
    }

    [Fact]
    public async Task InsecureHttpLogin_ShouldUseHostOnlyStrictCookiesAndNeverReturnRefreshTokenInJson()
    {
        await using var factory = new InsecureHttpRealJwtApiWebApplicationFactory();
        using var client = CreateClient(factory);
        using var response = await LoginAsync(client);

        var body = await response.ReadAsAsync<JsonElement>();
        body.GetProperty("data").TryGetProperty("refreshToken", out _).Should().BeFalse();

        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        var refreshCookie = cookies.Single(value =>
            value.StartsWith(AuthCookieTestHelper.InsecureRefreshCookieName + "=", StringComparison.Ordinal));
        var csrfCookie = cookies.Single(value =>
            value.StartsWith(AuthCookieTestHelper.CsrfCookieName + "=", StringComparison.Ordinal));

        AssertInsecureStrictHostOnlyCookie(refreshCookie, httpOnly: true);
        AssertInsecureStrictHostOnlyCookie(csrfCookie, httpOnly: false);
    }

    [Fact]
    public void InsecureHttpMode_WithValidExplicitConfiguration_ShouldPassValidation()
    {
        var options = CreateValidInsecureHttpOptions();

        var act = () => BrowserAuthConfigurationGuard.Validate(
            options,
            [AuthCookieTestHelper.AllowedOrigin],
            isProduction: true);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("secure")]
    [InlineData("lax")]
    [InlineData("none")]
    [InlineData("domain")]
    [InlineData("path")]
    [InlineData("host-refresh")]
    [InlineData("secure-refresh")]
    [InlineData("host-csrf")]
    [InlineData("secure-csrf")]
    public void InsecureHttpMode_WithUnsafeCookieConfiguration_ShouldFailValidation(string invalidCase)
    {
        var options = CreateValidInsecureHttpOptions();
        switch (invalidCase)
        {
            case "secure":
                options.CookieSecure = true;
                break;
            case "lax":
                options.CookieSameSite = SameSiteMode.Lax;
                break;
            case "none":
                options.CookieSameSite = SameSiteMode.None;
                break;
            case "domain":
                options.CookieDomain = "internal.example";
                break;
            case "path":
                options.CookiePath = "/auth";
                break;
            case "host-refresh":
                options.RefreshCookieName = "__Host-acceptance-refresh";
                break;
            case "secure-refresh":
                options.RefreshCookieName = "__Secure-acceptance-refresh";
                break;
            case "host-csrf":
                options.CsrfCookieName = "__Host-acceptance-csrf";
                break;
            case "secure-csrf":
                options.CsrfCookieName = "__Secure-acceptance-csrf";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidCase));
        }

        var act = () => BrowserAuthConfigurationGuard.Validate(
            options,
            [AuthCookieTestHelper.AllowedOrigin],
            isProduction: true);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("https://internal.example")]
    [InlineData("http://*.internal.example")]
    [InlineData("http://internal.example/login")]
    [InlineData("http://internal.example?source=test")]
    [InlineData("http://user@internal.example")]
    public void InsecureHttpMode_WithNonExactHttpOrigin_ShouldFailValidation(string origin)
    {
        var options = CreateValidInsecureHttpOptions();

        var act = () => BrowserAuthConfigurationGuard.Validate(options, [origin], isProduction: true);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProductionHttpOrigin_WithoutExplicitInsecureMode_ShouldFailValidation()
    {
        var options = new BrowserAuthOptions();

        var act = () => BrowserAuthConfigurationGuard.Validate(
            options,
            [AuthCookieTestHelper.AllowedOrigin],
            isProduction: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HTTPS*");
    }

    [Fact]
    public void ProductionHttpsDefaults_ShouldPassValidation()
    {
        var options = new BrowserAuthOptions();

        var act = () => BrowserAuthConfigurationGuard.Validate(
            options,
            ["https://internal.example"],
            isProduction: true);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("http://localhost", null, HttpStatusCode.OK)]
    [InlineData(null, "http://localhost/login", HttpStatusCode.OK)]
    [InlineData("https://evil.example", null, HttpStatusCode.Forbidden)]
    [InlineData(null, "https://evil.example/login", HttpStatusCode.Forbidden)]
    [InlineData(null, null, HttpStatusCode.Forbidden)]
    public async Task Login_BrowserOriginOrReferer_MustMatchAllowedOrigins(
        string? origin,
        string? referer,
        HttpStatusCode expectedStatus)
    {
        await using var factory = new RealJwtApiWebApplicationFactory();
        using var client = CreateClient(factory);
        using var request = AuthCookieTestHelper.CreateLoginRequest(
            "admin",
            ApiWebApplicationFactory.TestAdminPassword,
            origin,
            referer: referer);
        using var response = await client.SendAsync(request);

        var responseBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expectedStatus, responseBody);
        if (expectedStatus == HttpStatusCode.Forbidden)
            response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("BrowserAuth:CookieSecure", "false")]
    [InlineData("BrowserAuth:CookieDomain", "example.test")]
    [InlineData("BrowserAuth:CookiePath", "/auth")]
    public void HostPrefixedRefreshCookie_WithInvalidAttributes_ShouldFailStartup(string key, string value)
    {
        var options = new BrowserAuthOptions();
        switch (key)
        {
            case "BrowserAuth:CookieSecure":
                options.CookieSecure = bool.Parse(value);
                break;
            case "BrowserAuth:CookieDomain":
                options.CookieDomain = value;
                break;
            case "BrowserAuth:CookiePath":
                options.CookiePath = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key));
        }

        var act = () => BrowserAuthConfigurationGuard.Validate(
            options,
            [AuthCookieTestHelper.AllowedOrigin],
            isProduction: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*__Host-*");
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client)
    {
        using var request = AuthCookieTestHelper.CreateLoginRequest(
            "admin",
            ApiWebApplicationFactory.TestAdminPassword,
            origin: AuthCookieTestHelper.AllowedOrigin);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return response;
    }

    private static BrowserAuthOptions CreateValidInsecureHttpOptions() => new()
    {
        RefreshCookieName = AuthCookieTestHelper.InsecureRefreshCookieName,
        CsrfCookieName = AuthCookieTestHelper.CsrfCookieName,
        CookieSecure = false,
        CookieSameSite = SameSiteMode.Strict,
        CookiePath = "/",
        CookieDomain = null,
        AllowInsecureHttp = true
    };

    private static void AssertInsecureStrictHostOnlyCookie(string setCookie, bool httpOnly)
    {
        var attributes = setCookie.Split(';', StringSplitOptions.TrimEntries).Skip(1).ToArray();
        attributes.Should().Contain(attribute =>
            attribute.Equals("SameSite=Strict", StringComparison.OrdinalIgnoreCase));
        attributes.Should().NotContain(attribute =>
            attribute.Equals("Secure", StringComparison.OrdinalIgnoreCase));
        attributes.Should().NotContain(attribute =>
            attribute.StartsWith("Domain=", StringComparison.OrdinalIgnoreCase));
        if (httpOnly)
            attributes.Should().Contain(attribute => attribute.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase));
        else
            attributes.Should().NotContain(attribute => attribute.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase));
    }
}
