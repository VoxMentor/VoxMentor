using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Resume.AnalyzeResume;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Tests.Unit;

public class AnalyzeResumeHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private sealed class FakeTextExtractor : ITextExtractor
    {
        public string Text { get; set; } = string.Empty;
        public Exception? Throw { get; set; }
        public Task<string> ExtractAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            if (Throw is not null) throw Throw;
            return Task.FromResult(Text);
        }
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FakeCurrentUserService());
    }

    private static async Task<Guid> SeedJdAsync(ApplicationDbContext db, string userId, params (string skill, float weight)[] skills)
    {
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = "Acme",
            Role = "SDE-1",
            RawText = "Acme is hiring...",
            Difficulty = "Medium",
            EstimatedWeeks = 6
        };
        db.JobDescriptions.Add(jd);
        foreach (var (skill, weight) in skills)
        {
            db.JdSkillWeights.Add(new JdSkillWeight
            {
                Id = Guid.NewGuid(),
                JobDescriptionId = jd.Id,
                SkillName = skill,
                Weight = weight,
                IsTechnical = true
            });
        }
        await db.SaveChangesAsync();
        return jd.Id;
    }

    [Fact]
    public async Task Handle_NoUser_ThrowsUnauthorized()
    {
        var db = CreateDb();
        var extractor = new FakeTextExtractor { Text = "python" };
        var currentUser = new FakeCurrentUserService { UserId = null };
        var handler = new AnalyzeResumeHandler(db, currentUser, extractor);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UnknownJd_ThrowsNotFound()
    {
        var db = CreateDb();
        var extractor = new FakeTextExtractor { Text = "python" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_EmptyExtractedText_ThrowsValidation()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Python", 1f));
        var extractor = new FakeTextExtractor { Text = "   " };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CorruptExtraction_ThrowsValidation()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Python", 1f));
        var extractor = new FakeTextExtractor { Throw = new InvalidOperationException("bad pdf") };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_MatchesSkills_WeightedScoreAndGaps()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Python", 0.5f), ("SQL", 0.3f), ("Docker", 0.2f));
        var extractor = new FakeTextExtractor { Text = "Experienced Python and SQL developer" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.True(result.Success);
        var data = result.Data!;
        Assert.Equal(80, data.MatchScore);
        Assert.Contains("Python", data.KeywordMatch.Matched);
        Assert.Contains("SQL", data.KeywordMatch.Matched);
        Assert.Contains("Docker", data.KeywordMatch.Missing);
        Assert.DoesNotContain("Python", data.KeywordMatch.Missing);
        Assert.Contains(data.Suggestions, s => s.Contains("Docker"));
    }

    [Fact]
    public async Task Handle_WordBoundary_DoesNotFalsePositive()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Java", 1f));
        var extractor = new FakeTextExtractor { Text = "JavaScript developer" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Contains("Java", result.Data!.KeywordMatch.Missing);
        Assert.DoesNotContain("Java", result.Data.KeywordMatch.Matched);
    }

    [Fact]
    public async Task Handle_CaseInsensitiveMatch()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Python", 1f));
        var extractor = new FakeTextExtractor { Text = "PYTHON developer" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Contains("Python", result.Data!.KeywordMatch.Matched);
    }

    [Fact]
    public async Task Handle_SingleLetterSkill_DoesNotMatchCppOrCsharp()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("C", 1f));
        var extractor = new FakeTextExtractor { Text = "Experienced in C++ and C#" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Contains("C", result.Data!.KeywordMatch.Missing);
        Assert.DoesNotContain("C", result.Data.KeywordMatch.Matched);
    }

    [Fact]
    public async Task Handle_SkillWithRegexMetacharacters_MatchesLiterally()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Node.js", 0.5f), ("C++", 0.5f));
        var extractor = new FakeTextExtractor { Text = "Node.js and C++ developer" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Contains("Node.js", result.Data!.KeywordMatch.Matched);
        Assert.Contains("C++", result.Data.KeywordMatch.Matched);
        Assert.Equal(100, result.Data.MatchScore);
    }

    [Fact]
    public async Task Handle_OperationCanceled_Propagates()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Python", 1f));
        var extractor = new FakeTextExtractor { Throw = new OperationCanceledException() };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_HrSkills_CountInScore()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Python", 0.5f));
        db.JdSkillWeights.Add(new JdSkillWeight
        {
            Id = Guid.NewGuid(),
            JobDescriptionId = jdId,
            SkillName = "Leadership",
            Weight = 0.5f,
            IsTechnical = false
        });
        await db.SaveChangesAsync();

        var extractor = new FakeTextExtractor { Text = "Strong Python developer with leadership" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Equal(100, result.Data!.MatchScore);
        Assert.Contains("Python", result.Data.KeywordMatch.Matched);
        Assert.Contains("Leadership", result.Data.KeywordMatch.Matched);
    }

    [Fact]
    public async Task Handle_EmptySkillName_GoesToMissing()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("", 1f));
        var extractor = new FakeTextExtractor { Text = "anything" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Contains("", result.Data!.KeywordMatch.Missing);
        Assert.Equal(0, result.Data.MatchScore);
    }

    [Fact]
    public async Task Handle_AtScore_NeverBelowZero()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Python", 1f));
        // Text with no canonical sections at all.
        var extractor = new FakeTextExtractor { Text = "Python" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.True(result.Data!.AtsParseability.Score >= 0);
    }

    [Fact]
    public async Task Handle_MultiWordSkill_WrappedAcrossLines_Matches()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("System Design", 1f));
        var extractor = new FakeTextExtractor { Text = "System\r\nDesign experience" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Contains("System Design", result.Data!.KeywordMatch.Matched);
        Assert.Equal(100, result.Data.MatchScore);
    }

    [Fact]
    public async Task Handle_SkillWithIrregularWhitespace_MatchesNormalized()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("System  Design", 1f));
        var extractor = new FakeTextExtractor { Text = "System Design experience" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Contains("System  Design", result.Data!.KeywordMatch.Matched);
        Assert.Equal(100, result.Data.MatchScore);
    }

    [Fact]
    public async Task Handle_ZeroSkillJd_ScoreZero()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1");
        var extractor = new FakeTextExtractor { Text = "anything" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Equal(0, result.Data!.MatchScore);
        Assert.Empty(result.Data.KeywordMatch.Matched);
        Assert.Empty(result.Data.KeywordMatch.Missing);
    }

    [Fact]
    public async Task Handle_AtScoreReflectsMissingSections()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Python", 1f));
        var extractor = new FakeTextExtractor { Text = "python only, no sections" };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.True(result.Data!.AtsParseability.Issues.Count > 0);
        Assert.True(result.Data.AtsParseability.Score < 100);
        Assert.Contains(result.Data.Suggestions, s => s.Contains("section headings", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_ProseOnlySectionWords_DoNotCountAsHeadings()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Python", 1f));
        var extractor = new FakeTextExtractor
        {
            Text = "Education and experience gave me skills for projects, but no headings."
        };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Equal(4, result.Data!.AtsParseability.Issues.Count);
        Assert.Equal(40, result.Data.AtsParseability.Score);
    }

    [Fact]
    public async Task Handle_HeadingLines_CountAsSections()
    {
        var db = CreateDb();
        var jdId = await SeedJdAsync(db, "user-1", ("Python", 1f));
        var extractor = new FakeTextExtractor
        {
            Text = "Education\nBSc CS\n\nExperience\nDev at Acme\n\nSkills\nPython\n\nProjects\nFoo"
        };
        var handler = new AnalyzeResumeHandler(db, new FakeCurrentUserService(), extractor);

        var result = await handler.Handle(new AnalyzeResumeCommand("r.pdf", new MemoryStream(), 10, jdId), CancellationToken.None);

        Assert.Empty(result.Data!.AtsParseability.Issues);
        Assert.Equal(100, result.Data.AtsParseability.Score);
    }
}
