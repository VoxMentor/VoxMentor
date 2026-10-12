using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Features.Jd.UploadJd;

/// <summary>
/// Parses uploaded JD text (Ollama, degrading to keyword heuristics),
/// canonicalizes skill names against the concept vocabulary, normalizes
/// weights to sum to 1.0, and persists the JD + skill-weight rows in one
/// save (#117). No schema change — tables already exist.
/// </summary>
public class UploadJdHandler : IRequestHandler<UploadJdCommand, ApiResponse<UploadJdResultDto>>
{
    // Column limits from ApplicationDbContext (#117: no schema change) — the
    // LLM's output is untrusted against them.
    private const int MaxCompanyNameLength = 200;
    private const int MaxRoleLength = 200;
    private const int MaxDifficultyLength = 50;
    private const int MaxSkillNameLength = 100;

    private readonly IApplicationDbContext _db;
    private readonly IJdParseService _jdParser;
    private readonly ICurrentUserService _currentUser;

    public UploadJdHandler(IApplicationDbContext db, IJdParseService jdParser, ICurrentUserService currentUser)
    {
        _db = db;
        _jdParser = jdParser;
        _currentUser = currentUser;
    }

    /// <exception cref="UnauthorizedAccessException">No authenticated user.</exception>
    public async Task<ApiResponse<UploadJdResultDto>> Handle(
        UploadJdCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to upload a job description.");

        // The concept table is the vocabulary for both canonicalization and
        // the heuristic scan — no second list to keep in sync (#117 Q1).
        var conceptNames = await _db.Concepts
            .AsNoTracking()
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        // Ollama first; null = down or unparseable → degrade, never 503 (#117 Q3).
        var parsed = await _jdParser.ParseAsync(request.RawText, cancellationToken)
                     ?? JdHeuristics.Parse(request.RawText, conceptNames);

        // Canonicalize technical skills onto concept names so #116's ordinal
        // matching doesn't silently drop the rows (#117 Q4). Two raw names can
        // canonicalize to one concept ("DP" and "Dynamic Programming"), so sum
        // on collision rather than let ToDictionary throw. HR skills have no
        // concept vocabulary to match — they stay as the parser emitted them.
        var technicalSkills = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (skill, weight) in parsed.TechnicalSkills)
        {
            var canonical = Truncate(JdHeuristics.Canonicalize(skill, conceptNames), MaxSkillNameLength);
            if (canonical.Length == 0)
                continue;
            technicalSkills.TryGetValue(canonical, out var existing);
            technicalSkills[canonical] = existing + weight;
        }

        // Never trust LLM arithmetic — drop junk, renormalize to sum 1.0 (#117).
        var normalizedTechnical = JdWeightNormalizer.Normalize(technicalSkills);

        // The DB unique index is (JobDescriptionId, SkillName) regardless of
        // IsTechnical, so an HR key equal to any technical key would violate it
        // and 500 the save. Technical wins (readiness matching needs those rows);
        // drop collisions BEFORE normalizing so HR still sums to 1.0 on its own.
        var hrSkills = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (skill, weight) in parsed.HrSkills)
        {
            var key = Truncate(skill?.Trim(), MaxSkillNameLength);
            if (key.Length == 0 || normalizedTechnical.ContainsKey(key))
                continue;
            hrSkills.TryGetValue(key, out var existing);
            hrSkills[key] = existing + weight;
        }
        var normalizedHr = JdWeightNormalizer.Normalize(hrSkills);

        // LLM strings are untrusted against the column limits (CompanyName/Role
        // varchar(200), Difficulty varchar(50)) — an oversized value would turn
        // a successful parse into a DbUpdateException 500 (#117 review).
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = Truncate(parsed.CompanyName, MaxCompanyNameLength),
            Role = Truncate(parsed.Role, MaxRoleLength),
            RawText = request.RawText,
            Difficulty = Truncate(parsed.Difficulty, MaxDifficultyLength),
            EstimatedWeeks = parsed.EstimatedWeeks,
            CreatedAt = DateTime.UtcNow
        };
        _db.JobDescriptions.Add(jd);

        foreach (var (skill, weight) in normalizedTechnical)
        {
            _db.JdSkillWeights.Add(new JdSkillWeight
            {
                Id = Guid.NewGuid(),
                JobDescriptionId = jd.Id,
                SkillName = skill,
                Weight = weight,
                IsTechnical = true
            });
        }

        foreach (var (skill, weight) in normalizedHr)
        {
            _db.JdSkillWeights.Add(new JdSkillWeight
            {
                Id = Guid.NewGuid(),
                JobDescriptionId = jd.Id,
                SkillName = skill,
                Weight = weight,
                IsTechnical = false
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var dto = new UploadJdResultDto
        {
            JdId = jd.Id,
            CompanyName = jd.CompanyName,
            Role = jd.Role,
            TechnicalSkills = normalizedTechnical,
            HrSkills = normalizedHr,
            Difficulty = jd.Difficulty,
            EstimatedWeeks = jd.EstimatedWeeks,
            CreatedAt = jd.CreatedAt
        };

        return ApiResponse<UploadJdResultDto>.SuccessResult(dto, "Job description parsed and saved.");
    }

    /// <summary>Clamps untrusted parsed strings to their DB column limits.</summary>
    private static string Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value ?? string.Empty
            : value[..maxLength];
}
