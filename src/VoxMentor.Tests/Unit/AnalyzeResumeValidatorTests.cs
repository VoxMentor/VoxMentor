using VoxMentor.Application.Features.Resume.AnalyzeResume;

namespace VoxMentor.Tests.Unit;

/// <summary>FluentValidation rules for the resume analysis command (#151).</summary>
public class AnalyzeResumeValidatorTests
{
    private readonly AnalyzeResumeValidator _validator = new();

    private static AnalyzeResumeCommand Command(
        string fileName = "resume.pdf",
        Stream? content = null,
        long length = 1024,
        Guid? jdId = null) =>
        new(fileName, content ?? Stream.Null, length, jdId ?? Guid.NewGuid());

    [Fact]
    public void ValidCommand_Passes()
    {
        var result = _validator.Validate(Command(content: new MemoryStream(new byte[1]), length: 1));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData("resume.exe")]
    [InlineData("resume.png")]
    [InlineData("resume.txt")]
    [InlineData("noextension")]
    public void DisallowedExtensions_Fail(string fileName)
    {
        var result = _validator.Validate(Command(fileName));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Only .pdf files"));
    }

    [Fact]
    public void EmptyFile_Fails()
    {
        var result = _validator.Validate(Command("resume.pdf", length: 0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must not be empty"));
    }

    [Fact]
    public void OversizedFile_Fails()
    {
        var result = _validator.Validate(Command("big.pdf", length: AnalyzeResumeValidator.MaxFileSizeBytes + 1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("5242880")); // 5 MB
    }

    [Fact]
    public void FileAtLimit_Passes()
    {
        var result = _validator.Validate(Command("big.pdf", length: AnalyzeResumeValidator.MaxFileSizeBytes));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void MissingFileName_Fails()
    {
        var result = _validator.Validate(Command("", length: 100));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("File name is required"));
    }

    [Fact]
    public void FileNameOver260Chars_Fails()
    {
        var name = new string('a', 257) + ".pdf"; // 261 chars

        var result = _validator.Validate(Command(name));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("260 characters"));
    }

    [Fact]
    public void NullContent_Fails()
    {
        var result = _validator.Validate(new AnalyzeResumeCommand("notes.pdf", null!, 1024, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("File content is required"));
    }

    [Fact]
    public void MissingJdId_Fails()
    {
        var result = _validator.Validate(Command("resume.pdf", length: 100, jdId: Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("jdId"));
    }
}
