using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Api.DTOs;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class SystemUserPasswordValidationTests
{
    [Fact]
    public void CreateSystemUserRequest_WhenPasswordHasFourCharacters_ShouldPassValidation()
    {
        var request = new CreateSystemUserRequest
        {
            Username = "admin",
            Password = "admin",
            Nickname = "管理员",
            RoleCode = "admin",
            OrgUnitId = 1
        };

        var errors = Validate(request);

        errors.Should().NotContain(item => item.MemberNames.Contains(nameof(CreateSystemUserRequest.Password)));
    }

    [Fact]
    public void ResetSystemUserPasswordRequest_WhenPasswordHasFourCharacters_ShouldPassValidation()
    {
        var request = new ResetSystemUserPasswordRequest
        {
            NewPassword = "admin"
        };

        var errors = Validate(request);

        errors.Should().NotContain(item => item.MemberNames.Contains(nameof(ResetSystemUserPasswordRequest.NewPassword)));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
