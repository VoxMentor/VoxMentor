using VoxMentor.Application.Features.Jd.UploadJd;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="UploadJdValidator"/>: the 50–20000 char contract
/// from docs/API.md §3 (#117 done-when: 400 on out-of-range rawText).
/// </summary>
public class UploadJdValidatorTests
{
    private readonly UploadJdValidator _validator = new();

    [Fact]
    public void Validate_NullRawText_Fails()
    {
        var result = _validator.Validate(new UploadJdCommand(null!));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyRawText_Fails()
    {
        var result = _validator.Validate(new UploadJdCommand(string.Empty));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_49Chars_Fails()
    {
        var result = _validator.Validate(new UploadJdCommand(new string('a', 49)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_50Chars_Passes()
    {
        var result = _validator.Validate(new UploadJdCommand(new string('a', 50)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_20000Chars_Passes()
    {
        var result = _validator.Validate(new UploadJdCommand(new string('a', 20000)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_20001Chars_Fails()
    {
        var result = _validator.Validate(new UploadJdCommand(new string('a', 20001)));

        Assert.False(result.IsValid);
    }
}
