using VoxMentor.Application.Features.Admin.UploadTextbook;

namespace VoxMentor.Tests.Unit;

/// <summary>FluentValidation rules for the textbook upload command (#70).</summary>
public class UploadTextbookValidatorTests
{
    private readonly UploadTextbookValidator _validator = new();

    private static UploadTextbookCommand Command(
        string fileName, long length = 1024, Stream? content = null)
        => new(fileName, content ?? Stream.Null, length);

    [Theory]
    [InlineData("ctci.pdf")]
    [InlineData("notes.txt")]
    public void ValidFiles_Pass(string fileName)
    {
        var result = _validator.Validate(Command(fileName));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("image.png")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void DisallowedExtensions_Fail(string fileName)
    {
        var result = _validator.Validate(Command(fileName));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains(".pdf and .txt"));
    }

    [Fact]
    public void EmptyFile_Fails()
    {
        var result = _validator.Validate(Command("notes.txt", length: 0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must not be empty"));
    }

    [Fact]
    public void OversizedFile_Fails()
    {
        var result = _validator.Validate(Command("big.pdf", length: UploadTextbookValidator.MaxFileSizeBytes + 1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("smaller"));
    }

    [Fact]
    public void FileAtLimit_Passes()
    {
        var result = _validator.Validate(Command("big.pdf", length: UploadTextbookValidator.MaxFileSizeBytes));

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
        var name = new string('a', 257) + ".txt"; // 261 chars, valid extension

        var result = _validator.Validate(Command(name));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("260 characters"));
    }

    [Fact]
    public void FileNameAt260Chars_Passes()
    {
        var name = new string('a', 256) + ".txt"; // exactly 260 chars

        var result = _validator.Validate(Command(name));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void NullContent_Fails()
    {
        var result = _validator.Validate(new UploadTextbookCommand("notes.txt", null!, 1024));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("File content is required"));
    }
}
