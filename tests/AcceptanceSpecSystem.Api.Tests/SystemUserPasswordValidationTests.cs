using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Api.DTOs;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class SystemUserPasswordValidationTests
{
    [Fact]
    public void CreateCustomerRequest_WhenNameMissing_ShouldFailValidation()
    {
        var request = new CreateCustomerRequest();

        var errors = Validate(request);

        errors.Should().Contain(item => item.MemberNames.Contains(nameof(CreateCustomerRequest.Name)));
    }

    [Fact]
    public void UpdateCustomerRequest_WhenNameTooLong_ShouldFailValidation()
    {
        var request = new UpdateCustomerRequest
        {
            Name = new string('客', 101)
        };

        var errors = Validate(request);

        errors.Should().Contain(item => item.MemberNames.Contains(nameof(UpdateCustomerRequest.Name)));
    }

    [Fact]
    public void CreateMachineModelRequest_WhenNameTooLong_ShouldFailValidation()
    {
        var request = new CreateMachineModelRequest
        {
            Name = new string('机', 101)
        };

        var errors = Validate(request);

        errors.Should().Contain(item => item.MemberNames.Contains(nameof(CreateMachineModelRequest.Name)));
    }

    [Fact]
    public void CreateProcessRequest_WhenNameMissing_ShouldFailValidation()
    {
        var request = new CreateProcessRequest();

        var errors = Validate(request);

        errors.Should().Contain(item => item.MemberNames.Contains(nameof(CreateProcessRequest.Name)));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(200, false)]
    [InlineData(201, true)]
    public void CreateSystemUserRequest_ShouldEnforcePasswordLengthBoundary(
        int passwordLength,
        bool shouldHaveValidationError)
    {
        var request = new CreateSystemUserRequest
        {
            Username = "admin",
            Password = new string('p', passwordLength),
            Nickname = "管理员",
            RoleCode = "admin",
            OrgUnitId = 1
        };

        var errors = Validate(request);

        errors.Any(item => item.MemberNames.Contains(nameof(CreateSystemUserRequest.Password)))
            .Should().Be(shouldHaveValidationError);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(200, false)]
    [InlineData(201, true)]
    public void ResetSystemUserPasswordRequest_ShouldEnforcePasswordLengthBoundary(
        int passwordLength,
        bool shouldHaveValidationError)
    {
        var request = new ResetSystemUserPasswordRequest
        {
            NewPassword = new string('p', passwordLength)
        };

        var errors = Validate(request);

        errors.Any(item => item.MemberNames.Contains(nameof(ResetSystemUserPasswordRequest.NewPassword)))
            .Should().Be(shouldHaveValidationError);
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
