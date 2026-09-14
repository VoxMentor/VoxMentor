using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Admin.CreateQuestion;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="CreateQuestionHandler"/> and <see cref="CreateQuestionValidator"/>
/// covering happy path, concept validation, auth, and input validation.
/// </summary>
public class CreateQuestionHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "admin-1";
    }

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb(string? sharedName = null)
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(sharedName ?? Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    private static async Task<Concept> SeedConceptAsync(Infrastructure.Persistence.ApplicationDbContext db)
    {
        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = "Arrays",
            Description = "Contiguous indexed storage.",
            DifficultyLevel = 2,
            Category = "Data Structures"
        };
        db.Concepts.Add(concept);
        await db.SaveChangesAsync();
        return concept;
    }

    private static CreateQuestionHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUserService? user = null)
    {
        return new CreateQuestionHandler(db, user ?? new FakeCurrentUserService());
    }

    private const string ValidCase = "{\"input\":\"[1,2] 3\",\"expected\":\"[0,1]\"}";
    private const string ValidHiddenCase = "{\"input\":\"[3,4] 7\",\"expected\":\"[1,2]\",\"hidden\":true}";

    // ==================== Handler Tests ====================

    [Fact]
    public async Task Handle_ValidQuestion_CreatesAndReturns()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        var handler = CreateHandler(db);

        var result = await handler.Handle(new CreateQuestionCommand(
            concept.Id, "Two Sum", "Find two numbers that add to target.", 2,
            new[] { ValidCase },
            new[] { "[1,2]" }, new[] { "[0,1]" }, null), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Two Sum", result.Data!.Title);
        Assert.Equal(concept.Id, result.Data.ConceptId);
        Assert.Equal(2, result.Data.Difficulty);

        var stored = await db.Questions.FirstAsync(q => q.Title == "Two Sum");
        Assert.Equal(concept.Id, stored.ConceptId);
        Assert.Single(stored.TestCases);
        Assert.Equal("Code", stored.QuestionType);
        Assert.Empty(stored.Rubric);
        Assert.Equal(0, stored.HiddenTestCaseCount);
    }

    [Fact]
    public async Task Handle_HiddenTestCase_PersistsHiddenCount()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        var handler = CreateHandler(db);

        var result = await handler.Handle(new CreateQuestionCommand(
            concept.Id, "Two Sum", "Find two numbers that add to target.", 2,
            new[] { ValidCase, ValidHiddenCase },
            null, null, null), CancellationToken.None);

        Assert.True(result.Success);
        var stored = await db.Questions.FirstAsync(q => q.Title == "Two Sum");
        Assert.Equal(1, stored.HiddenTestCaseCount);
    }

    [Fact]
    public async Task Handle_QuestionTypeAndRubric_Persisted()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        var handler = CreateHandler(db);

        var result = await handler.Handle(new CreateQuestionCommand(
            concept.Id, "MCQ Question", "Pick one.", 3,
            new[] { ValidCase },
            null, null, null,
            "MCQ", new[] { "{\"criterion\":\"Handles edge cases\",\"points\":2}" }), CancellationToken.None);

        Assert.True(result.Success);
        var stored = await db.Questions.FirstAsync(q => q.Title == "MCQ Question");
        Assert.Equal("MCQ", stored.QuestionType);
        Assert.Single(stored.Rubric);
    }

    [Fact]
    public async Task Handle_MissingConcept_ThrowsNotFound()
    {
        using var db = CreateDb();
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<Application.Common.Exceptions.NotFoundException>(
            () => handler.Handle(new CreateQuestionCommand(
                Guid.NewGuid(), "Test", "Desc", 1,
                new[] { ValidCase }, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_ThrowsUnauthorized()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        var handler = CreateHandler(db, new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.Handle(new CreateQuestionCommand(
                concept.Id, "Test", "Desc", 1,
                new[] { ValidCase }, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NullOptionalFields_DefaultsToEmpty()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        var handler = CreateHandler(db);

        var result = await handler.Handle(new CreateQuestionCommand(
            concept.Id, "Minimal", "Desc", 1,
            new[] { ValidCase }, null, null, null), CancellationToken.None);

        Assert.True(result.Success);
        var stored = await db.Questions.FirstAsync(q => q.Title == "Minimal");
        Assert.Empty(stored.ExampleInputs);
        Assert.Empty(stored.ExampleOutputs);
        Assert.Empty(stored.StarterCode);
        Assert.Empty(stored.Rubric);
    }

    [Fact]
    public async Task Handle_InvalidTestCases_ThrowsValidationException()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<Application.Common.Exceptions.ValidationException>(
            () => handler.Handle(new CreateQuestionCommand(
                concept.Id, "Bad", "Desc", 1,
                new[] { "{}" }, null, null, null), CancellationToken.None));
    }

    // ==================== Validator Tests ====================

    [Fact]
    public void Validator_ValidCommand_Passes()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { ValidCase, ValidHiddenCase }, null, null, null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_EmptyConceptId_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.Empty, "Test", "Description", 5,
            new[] { ValidCase }, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.ConceptId));
    }

    [Fact]
    public void Validator_EmptyTitle_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "", "Description", 5,
            new[] { ValidCase }, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.Title));
    }

    [Fact]
    public void Validator_TitleTooLong_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), new string('a', 301), "Description", 5,
            new[] { ValidCase }, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.Title));
    }

    [Fact]
    public void Validator_DifficultyZero_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 0,
            new[] { ValidCase }, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.Difficulty));
    }

    [Fact]
    public void Validator_DifficultyEleven_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 11,
            new[] { ValidCase }, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.Difficulty));
    }

    [Fact]
    public void Validator_EmptyTestCases_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            Array.Empty<string>(), null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.TestCases));
    }

    [Fact]
    public void Validator_EmptyTestCaseEntry_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { "" }, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.TestCases));
    }

    [Fact]
    public void Validator_TestCaseNotJsonObject_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { "\"just a string\"" }, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.TestCases));
    }

    [Fact]
    public void Validator_TestCaseMissingExpected_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { "{\"input\":\"1 2\"}" }, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.TestCases));
    }

    [Fact]
    public void Validator_TestCaseHiddenNotBoolean_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { "{\"input\":\"1\",\"expected\":\"2\",\"hidden\":\"yes\"}" }, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.TestCases));
    }

    [Fact]
    public void Validator_NonTrailingHiddenCase_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { ValidHiddenCase, ValidCase }, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.TestCases));
    }

    [Fact]
    public void Validator_EmptyQuestionType_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { ValidCase }, null, null, null, ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.QuestionType));
    }

    [Fact]
    public void Validator_QuestionTypeTooLong_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { ValidCase }, null, null, null, new string('a', 51)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.QuestionType));
    }

    [Fact]
    public void Validator_RubricNotJson_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { ValidCase }, null, null, null, "Code", new[] { "invalid" }));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.Rubric));
    }

    [Fact]
    public void Validator_RubricMissingCriterion_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { ValidCase }, null, null, null, "Code",
            new[] { "{\"points\":2}" }));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.Rubric));
    }

    [Fact]
    public void Validator_RubricNonNumericPoints_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { ValidCase }, null, null, null, "Code",
            new[] { "{\"criterion\":\"Handles edge cases\",\"points\":\"many\"}" }));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.Rubric));
    }

    [Fact]
    public void Validator_ValidRubric_Passes()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new[] { ValidCase }, null, null, null, "Code",
            new[] { "{\"criterion\":\"Handles edge cases\",\"points\":2}" }));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_NullTestCaseElement_Fails()
    {
        var validator = new CreateQuestionValidator();
        var result = validator.Validate(new CreateQuestionCommand(
            Guid.NewGuid(), "Test", "Description", 5,
            new string?[] { null }!, null, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateQuestionCommand.TestCases));
    }

    [Fact]
    public async Task Handle_NullTestCaseElement_ThrowsValidationException()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<Application.Common.Exceptions.ValidationException>(
            () => handler.Handle(new CreateQuestionCommand(
                concept.Id, "Bad", "Desc", 1,
                new string?[] { null }!, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_InvalidRubric_ThrowsValidationException()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<Application.Common.Exceptions.ValidationException>(
            () => handler.Handle(new CreateQuestionCommand(
                concept.Id, "Bad", "Desc", 1,
                new[] { ValidCase }, null, null, null, "Code", new[] { "invalid" }),
                CancellationToken.None));
    }
}
