using AcceptanceSpecSystem.Api.Options;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthSeedOptionsValidationTests
{
    [Fact]
    public void Validate_WhenProductionPasswordTooShort_ShouldFail()
    {
        var validator = new AuthSeedOptionsValidator(new FakeHostEnvironment
        {
            EnvironmentName = Environments.Production
        });

        var result = validator.Validate(AuthSeedOptions.SectionName, new AuthSeedOptions
        {
            AdminPassword = "short",
            CommonPassword = "CommonPassword!2026"
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("AdminPassword", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenDevelopmentPasswordsAreMissing_ShouldSucceed()
    {
        var validator = new AuthSeedOptionsValidator(new FakeHostEnvironment
        {
            EnvironmentName = Environments.Development
        });

        var result = validator.Validate(AuthSeedOptions.SectionName, new AuthSeedOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenDevelopmentAdminPasswordIsShort_ShouldSucceed()
    {
        var validator = new AuthSeedOptionsValidator(new FakeHostEnvironment
        {
            EnvironmentName = Environments.Development
        });

        var result = validator.Validate(AuthSeedOptions.SectionName, new AuthSeedOptions
        {
            AdminPassword = "admin"
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenConfiguredPasswordsMeetLengthRequirement_ShouldSucceed()
    {
        var validator = new AuthSeedOptionsValidator(new FakeHostEnvironment
        {
            EnvironmentName = Environments.Production
        });

        var result = validator.Validate(AuthSeedOptions.SectionName, new AuthSeedOptions
        {
            AdminPassword = "AdminPassword!2026",
            CommonPassword = "CommonPassword!2026"
        });

        result.Succeeded.Should().BeTrue();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "AcceptanceSpecSystem.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
