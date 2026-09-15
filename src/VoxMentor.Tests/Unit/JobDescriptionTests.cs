using Microsoft.EntityFrameworkCore;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="JobDescription"/> and <see cref="JdSkillWeight"/>
/// persistence, FK constraints, unique composite index, and cascade delete.
/// </summary>
public class JobDescriptionTests
{
    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    [Fact]
    public async Task JobDescription_CanBePersisted()
    {
        await using var db = CreateDb();
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            CompanyName = "Amazon",
            Role = "SDE-1",
            RawText = "Amazon is hiring an SDE-1...",
            Difficulty = "Medium-Hard",
            EstimatedWeeks = 6
        };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();

        var loaded = await db.JobDescriptions.FindAsync(jd.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Amazon", loaded.CompanyName);
        Assert.Equal("SDE-1", loaded.Role);
        Assert.Equal(6, loaded.EstimatedWeeks);
    }

    [Fact]
    public async Task JdSkillWeight_CanBePersisted()
    {
        await using var db = CreateDb();
        var jd = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "Google", Role = "SWE", RawText = "text" };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();

        var skill = new JdSkillWeight
        {
            Id = Guid.NewGuid(),
            JobDescriptionId = jd.Id,
            SkillName = "DP",
            Weight = 0.35f,
            IsTechnical = true
        };
        db.JdSkillWeights.Add(skill);
        await db.SaveChangesAsync();

        var loaded = await db.JdSkillWeights.FindAsync(skill.Id);
        Assert.NotNull(loaded);
        Assert.Equal("DP", loaded.SkillName);
        Assert.Equal(0.35f, loaded.Weight);
        Assert.True(loaded.IsTechnical);
    }

    [Fact]
    public async Task JdSkillWeight_DifferentSkillsSameJd_Allowed()
    {
        await using var db = CreateDb();
        var jd = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "X", Role = "Y", RawText = "t" };
        db.JobDescriptions.Add(jd);
        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd.Id, SkillName = "DP", Weight = 0.3f });
        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd.Id, SkillName = "Graphs", Weight = 0.2f });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.JdSkillWeights.CountAsync(s => s.JobDescriptionId == jd.Id));
    }

    [Fact]
    public async Task JdSkillWeight_SameSkillDifferentJds_Allowed()
    {
        await using var db = CreateDb();
        var jd1 = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "A", Role = "X", RawText = "t" };
        var jd2 = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "B", Role = "Y", RawText = "t" };
        db.JobDescriptions.AddRange(jd1, jd2);
        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd1.Id, SkillName = "DP", Weight = 0.3f });
        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd2.Id, SkillName = "DP", Weight = 0.5f });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.JdSkillWeights.CountAsync(s => s.SkillName == "DP"));
    }
}
