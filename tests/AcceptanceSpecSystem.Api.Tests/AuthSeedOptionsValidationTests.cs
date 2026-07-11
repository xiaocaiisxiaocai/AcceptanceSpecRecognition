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

    [Theory]
    [InlineData("replace_with_at_least_12_chars_admin_password")]
    [InlineData("ChangeThisAdminPassword_2026")]
    [InlineData("__REQUIRED_ADMIN_PASSWORD__")]
    public void Validate_WhenProductionPasswordIsKnownPlaceholder_ShouldFail(string placeholder)
    {
        var validator = new AuthSeedOptionsValidator(new FakeHostEnvironment
        {
            EnvironmentName = Environments.Production
        });

        var result = validator.Validate(AuthSeedOptions.SectionName, new AuthSeedOptions
        {
            AdminPassword = placeholder,
            CommonPassword = "CommonPassword!2026"
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure =>
            failure.Contains("占位符", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("replace_with_at_least_32_chars_random_key")]
    [InlineData("ChangeThisToLongRandomSecretAtLeast32Chars")]
    [InlineData("__REQUIRED_DATABASE_PASSWORD__")]
    [InlineData("your_secret_value")]
    public void ProductionSecretGuard_WhenValueIsKnownPlaceholder_ShouldReject(string placeholder)
    {
        ProductionSecretGuard.IsKnownPlaceholder(placeholder).Should().BeTrue();
    }

    [Fact]
    public void ProductionSecretGuard_WhenValueIsRealRandomSecret_ShouldAccept()
    {
        ProductionSecretGuard.IsKnownPlaceholder("9v!Qx2#Lp7@Zm4$Rt8%Ks6&Wn3*Hy5").Should().BeFalse();
    }

    [Theory]
    [InlineData("Server=mysql;User=acceptance;Password=replace_with_app_password;")]
    [InlineData("Server=mysql;User=acceptance;Pwd=ChangeThisDatabasePassword_2026;")]
    public void ProductionSecretGuard_WhenConnectionStringUsesPlaceholderPassword_ShouldReject(
        string connectionString)
    {
        ProductionSecretGuard.HasKnownPlaceholderPassword(connectionString).Should().BeTrue();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "AcceptanceSpecSystem.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
