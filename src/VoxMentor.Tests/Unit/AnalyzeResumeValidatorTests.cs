using VoxMentor.Application.Features.Resume.AnalyzeResume;

namespace VoxMentor.Tests.Unit;

public class AnalyzeResumeValidatorTests
{
    private readonly AnalyzeResumeValidator _validator = new();

    private static AnalyzeResumeCommand Command(
        string fileName = "resume.pdf",
        long length = 100,
        Guid? jdId = null) =>
        new(fileName, new MemoryStream(new byte[length]), length, jdId ?? Guid.NewGuid());

    [Fact]
    public void Validate_NullFileName_Fails()
    {
        var result = _validator.Validate(Command(fileName: null!));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyFileName_Fails()
    {
        var result = _validator.Validate(Command(fileName: string.Empty));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("resume.exe")]
    [InlineData("resume.docx")]
    [InlineData("resume")]
    public void Validate_UnsupportedExtension_Fails(string fileName)
    {
        var result = _validator.Validate(Command(fileName: fileName));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains(".pdf and .txt"));
    }

    [Theory]
    [InlineData("resume.pdf")]
    [InlineData("resume.txt")]
    public void Validate_SupportedExtension_Passes(string fileName)
    {
        var result = _validator.Validate(Command(fileName: fileName));
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Validate_EmptyFile_Fails()
    {
        var result = _validator.Validate(Command(length: 0));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("empty"));
    }

    [Fact]
    public void Validate_OversizedFile_Fails()
    {
        var result = _validator.Validate(Command(length: AnalyzeResumeValidator.MaxFileSizeBytes + 1));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_MaxSizeFile_Passes()
    {
        var result = _validator.Validate(Command(length: AnalyzeResumeValidator.MaxFileSizeBytes));
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Validate_EmptyJdId_Fails()
    {
        var result = _validator.Validate(Command(jdId: Guid.Empty));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NullContent_Fails()
    {
        var result = _validator.Validate(new AnalyzeResumeCommand("resume.pdf", null!, 100, Guid.NewGuid()));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("content"));
    }

    [Fact]
    public void Validate_OversizedFileName_Fails()
    {
        var fileName = new string('a', 261) + ".pdf";
        var result = _validator.Validate(Command(fileName: fileName));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("260"));
    }

    [Fact]
    public void Validate_UppercaseExtension_Passes()
    {
        var result = _validator.Validate(Command(fileName: "resume.PDF"));
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }
}
