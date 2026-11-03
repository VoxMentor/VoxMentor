using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Resume.AnalyzeResume;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="AnalyzeResumeHandler"/>: ownership 404s,
/// weighted skill matching, ATS parseability, and persistence (#151).
/// </summary>
public class AnalyzeResumeHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private sealed class FakeTextExtractor : ITextExtractor
    {
        public string? TextToReturn { get; set; }
        public Task<string> ExtractAsync(Stream stream, string extension, CancellationToken cancellationToken = default)
            => Task.FromResult(TextToReturn ?? string.Empty);
    }

    /// <summary>Allows every request — rate limiting is covered by the API integration tests.</summary>
    private sealed class FakeRateLimiter : IRateLimiter
    {
        public Task CheckAsync(string key, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options, new FakeCurrentUserService());
    }

    private static async Task<JobDescription> SeedJdAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string userId,
        string company = "Amazon")
    {
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = company,
            Role = "SDE-1",
            RawText = $"{company} is hiring...",
            Difficulty = "Medium",
            EstimatedWeeks = 4,
            CreatedAt = DateTime.UtcNow
        };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();
        return jd;
    }

    private static async Task SeedSkillAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        Guid jdId,
        string skillName,
        float weight,
        bool isTechnical = true)
    {
        db.JdSkillWeights.Add(new JdSkillWeight
        {
            Id = Guid.NewGuid(),
            JobDescriptionId = jdId,
            SkillName = skillName,
            Weight = weight,
            IsTechnical = isTechnical
        });
        await db.SaveChangesAsync();
    }

    private static AnalyzeResumeHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeTextExtractor extractor,
        FakeCurrentUserService? user = null)
    {
        return new AnalyzeResumeHandler(
            db,
            extractor,
            user ?? new FakeCurrentUserService(),
            new FakeRateLimiter(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalyzeResumeHandler>.Instance);
    }

    private static AnalyzeResumeCommand Command(Guid jdId, string fileName = "resume.pdf") =>
        new(fileName, new MemoryStream(), 0, jdId);

    [Fact]
    public async Task Handle_UnauthenticatedUser_Throws()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        var handler = CreateHandler(db, new FakeTextExtractor(), new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(Command(jd.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_JdNotFound_ThrowsNotFound()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db, new FakeTextExtractor());

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(Command(Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("job description", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_JdNotOwnedByUser_ThrowsNotFound()
    {
        await using var db = CreateDb();
        var foreignJd = await SeedJdAsync(db, userId: "other-user");
        var handler = CreateHandler(db, new FakeTextExtractor()); // current user = "user-1"

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(Command(foreignJd.Id), CancellationToken.None));
        Assert.Contains("job description", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_HappyPath_ReturnsWeightedScoreAndMatchedMissing()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillAsync(db, jd.Id, "Python", 0.5f);
        await SeedSkillAsync(db, jd.Id, "Databases", 0.3f);
        await SeedSkillAsync(db, jd.Id, "Teamwork", 0.2f, isTechnical: false);

        var extractor = new FakeTextExtractor { TextToReturn = "Python developer with Databases and teamwork experience." };
        var handler = CreateHandler(db, extractor);

        var response = await handler.Handle(Command(jd.Id), CancellationToken.None);

        Assert.True(response.Success);
        var dto = response.Data!;
        // matched: Python 0.5 + Databases 0.3 + Teamwork 0.2 = 1.0 → 100
        Assert.Equal(100, dto.MatchScore);
        Assert.Equal(new[] { "Python", "Databases", "Teamwork" }, dto.KeywordMatch.Matched, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(dto.KeywordMatch.Missing);
    }

    [Fact]
    public async Task Handle_PartialMatch_ReturnsWeightedScore()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillAsync(db, jd.Id, "Python", 0.5f);
        await SeedSkillAsync(db, jd.Id, "Databases", 0.3f);
        await SeedSkillAsync(db, jd.Id, "Teamwork", 0.2f, isTechnical: false);

        var extractor = new FakeTextExtractor { TextToReturn = "Python developer." };
        var handler = CreateHandler(db, extractor);

        var response = await handler.Handle(Command(jd.Id), CancellationToken.None);
        var dto = response.Data!;

        // only Python matched → 0.5 / 1.0 = 50
        Assert.Equal(50, dto.MatchScore);
        Assert.Equal(new[] { "Python" }, dto.KeywordMatch.Matched);
        Assert.Equal(new[] { "Databases", "Teamwork" }, dto.KeywordMatch.Missing);
    }

    [Fact]
    public async Task Handle_SingleLetterSkill_DoesNotMatchCppOrCsharp()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillAsync(db, jd.Id, "C", 1.0f);

        var extractor = new FakeTextExtractor
            { TextToReturn = "Experienced in C++ and C# development." };
        var handler = CreateHandler(db, extractor);

        var response = await handler.Handle(Command(jd.Id), CancellationToken.None);
        var dto = response.Data!;

        Assert.Equal(0, dto.MatchScore);
        Assert.Empty(dto.KeywordMatch.Matched);
        Assert.Equal(new[] { "C" }, dto.KeywordMatch.Missing);
    }

    [Fact]
    public async Task Handle_SkillWithIrregularWhitespace_MatchesNormalized()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        // "System  Design" stored with double space in the JD.
        await SeedSkillAsync(db, jd.Id, "System  Design", 1.0f);

        var extractor = new FakeTextExtractor { TextToReturn = "System Design on AWS." };
        var handler = CreateHandler(db, extractor);

        var response = await handler.Handle(Command(jd.Id), CancellationToken.None);
        var dto = response.Data!;

        Assert.Equal(100, dto.MatchScore);
        Assert.Contains("System  Design", dto.KeywordMatch.Matched, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Handle_NoSkills_Returns100AndEmptyCollections()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");

        var extractor = new FakeTextExtractor { TextToReturn = "Anything at all." };
        var handler = CreateHandler(db, extractor);

        var response = await handler.Handle(Command(jd.Id), CancellationToken.None);
        var dto = response.Data!;

        Assert.Equal(100, dto.MatchScore);
        Assert.Empty(dto.KeywordMatch.Matched);
        Assert.Empty(dto.KeywordMatch.Missing);
    }

    [Fact]
    public async Task Handle_MissingLabeledSections_LowersAtsScore()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillAsync(db, jd.Id, "Python", 1.0f);

        // Resume has none of the labeled section headings.
        var extractor = new FakeTextExtractor { TextToReturn = "Python developer looking for work." };
        var handler = CreateHandler(db, extractor);

        var response = await handler.Handle(Command(jd.Id), CancellationToken.None);
        var dto = response.Data!;

        // 4 missing sections × 15 = 60 penalty → 40
        Assert.Equal(40, dto.AtsParseability.Score);
        Assert.Equal(4, dto.AtsParseability.Issues.Count);
    }

    [Fact]
    public async Task Handle_HeadingLineDetection_CountsLabeledSections()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillAsync(db, jd.Id, "Python", 1.0f);

        var extractor = new FakeTextExtractor
        {
            TextToReturn = "Education\nPython\n\nExperience\nWorked at X.\n\nSkills\nPython, AWS.\n\nProjects\nBuilt a thing."
        };
        var handler = CreateHandler(db, extractor);

        var response = await handler.Handle(Command(jd.Id), CancellationToken.None);
        var dto = response.Data!;

        Assert.Equal(100, dto.AtsParseability.Score);
        Assert.Empty(dto.AtsParseability.Issues);
    }

    [Fact]
    public async Task Handle_PersistsResumeAnalysisRow()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillAsync(db, jd.Id, "Python", 1.0f);

        var extractor = new FakeTextExtractor { TextToReturn = "Python developer." };
        var handler = CreateHandler(db, extractor);

        var response = await handler.Handle(Command(jd.Id, "my-resume.pdf"), CancellationToken.None);
        var dto = response.Data!;

        var row = await db.ResumeAnalyses
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == dto.AnalysisId, CancellationToken.None);

        Assert.NotNull(row);
        Assert.Equal("user-1", row.UserId);
        Assert.Equal(jd.Id, row.JdId);
        Assert.Equal("my-resume.pdf", row.FileName);
        Assert.Equal(100, row.MatchScore);
    }
}
