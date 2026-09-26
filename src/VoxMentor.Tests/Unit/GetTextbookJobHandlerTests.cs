using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Features.Admin.GetTextbookJob;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Tests.Unit;

/// <summary>Status polling handler coverage (#70).</summary>
public class GetTextbookJobHandlerTests
{
    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    [Fact]
    public async Task Found_MapsAllFields()
    {
        var db = CreateDb();
        var createdAt = new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc);
        var completedAt = new DateTime(2026, 9, 25, 8, 5, 0, DateTimeKind.Utc);
        var job = new TextbookJob
        {
            Id = Guid.NewGuid(),
            FileName = "ctci.pdf",
            Status = TextbookJobStatus.Completed,
            TotalChunks = 240,
            ProcessedChunks = 240,
            Error = null,
            CreatedAt = createdAt,
            CompletedAt = completedAt
        };
        db.TextbookJobs.Add(job);
        await db.SaveChangesAsync();

        var handler = new GetTextbookJobHandler(db);
        var result = await handler.Handle(new GetTextbookJobQuery(job.Id), CancellationToken.None);

        Assert.NotNull(result.Data);
        var dto = result.Data!;
        Assert.Equal(job.Id, dto.JobId);
        Assert.Equal("ctci.pdf", dto.FileName);
        Assert.Equal(TextbookJobStatus.Completed, dto.Status);
        Assert.Equal(240, dto.TotalChunks);
        Assert.Equal(240, dto.ProcessedChunks);
        Assert.Null(dto.Error);
        Assert.Equal(createdAt, dto.CreatedAt);
        Assert.Equal(completedAt, dto.CompletedAt);
    }

    [Fact]
    public async Task FailedJob_ReportsError()
    {
        var db = CreateDb();
        db.TextbookJobs.Add(new TextbookJob
        {
            Id = Guid.NewGuid(),
            FileName = "broken.pdf",
            Status = TextbookJobStatus.Failed,
            Error = "Ollama unavailable"
        });
        await db.SaveChangesAsync();

        var handler = new GetTextbookJobHandler(db);
        var result = await handler.Handle(
            new GetTextbookJobQuery(db.TextbookJobs.AsNoTracking().First().Id), CancellationToken.None);

        Assert.Equal(TextbookJobStatus.Failed, result.Data!.Status);
        Assert.Equal("Ollama unavailable", result.Data.Error);
    }

    [Fact]
    public async Task UnknownJob_ThrowsNotFound()
    {
        var db = CreateDb();
        var handler = new GetTextbookJobHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetTextbookJobQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public void Status_SerializesAsJsonString_MatchingApiDocsContract()
    {
        // docs/API.md documents "status": "Pending | Processing | Completed | Failed".
        var dto = new TextbookJobDto(
            Guid.NewGuid(), "ctci.pdf", TextbookJobStatus.Processing, 240, 118,
            null, DateTime.UtcNow, null);

        var json = System.Text.Json.JsonSerializer.Serialize(
            dto,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

        Assert.Contains("\"status\":\"Processing\"", json);
        Assert.DoesNotContain("\"status\":0", json);
    }
}
