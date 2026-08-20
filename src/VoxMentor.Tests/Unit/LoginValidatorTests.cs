using FluentValidation.TestHelper;
using VoxMentor.Application.Features.Auth.Login;
using Xunit;

namespace VoxMentor.Tests.Unit;

public class LoginValidatorTests
{
    private readonly LoginValidator _validator;

    public LoginValidatorTests()
    {
        _validator = new LoginValidator();
    }

    [Fact]
    public void Should_Pass_When_Command_Is_Valid()
    {
        var command = new LoginCommand("ansif@example.com", "Password@123");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Fail_When_Email_Is_Empty()
    {
        var command = new LoginCommand("", "Password@123");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Should_Fail_When_Email_Format_Is_Invalid()
    {
        var command = new LoginCommand("invalid-email", "Password@123");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Should_Fail_When_Password_Is_Empty()
    {
        var command = new LoginCommand("ansif@example.com", "");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }
}
