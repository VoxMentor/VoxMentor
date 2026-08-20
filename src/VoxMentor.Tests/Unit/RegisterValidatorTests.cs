using FluentValidation.TestHelper;
using VoxMentor.Application.Features.Auth.Register;
using Xunit;

namespace VoxMentor.Tests.Unit;

public class RegisterValidatorTests
{
    private readonly RegisterValidator _validator;

    public RegisterValidatorTests()
    {
        _validator = new RegisterValidator();
    }

    [Fact]
    public void Should_Pass_When_Command_Is_Valid()
    {
        var command = new RegisterCommand("Ansif Mk", "ansif@example.com", "Password@123");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Fail_When_FullName_Is_Empty()
    {
        var command = new RegisterCommand("", "ansif@example.com", "Password@123");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.FullName);
    }

    [Fact]
    public void Should_Fail_When_Email_Is_Empty()
    {
        var command = new RegisterCommand("Ansif Mk", "", "Password@123");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Should_Fail_When_Email_Is_Invalid()
    {
        var command = new RegisterCommand("Ansif Mk", "invalid-email-format", "Password@123");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Should_Fail_When_Password_Is_Empty()
    {
        var command = new RegisterCommand("Ansif Mk", "ansif@example.com", "");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Theory]
    [InlineData("short")] // Less than 8 chars
    [InlineData("alllowercase123!")] // No uppercase
    [InlineData("ALLUPPERCASE123!")] // No lowercase
    [InlineData("NoDigitPassword!")] // No digit
    [InlineData("NoSpecialChar123")] // No special character
    public void Should_Fail_When_Password_Is_Weak(string weakPassword)
    {
        var command = new RegisterCommand("Ansif Mk", "ansif@example.com", weakPassword);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }
}
