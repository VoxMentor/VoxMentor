using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Features.Resume.AnalyzeResume;

/// <summary>
/// Analyzes an uploaded resume (.pdf) against a JD's weighted skills and
/// returns an ATS fit report (#151).
/// </summary>
public class AnalyzeResumeHandler : IRequestHandler<AnalyzeResumeCommand, ApiResponse<AnalyzeResumeResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ITextExtractor _extractor;
    private readonly ICurrentUserService _currentUser;
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<AnalyzeResumeHandler> _logger;

    /// <summary>ponytail: 4 missing labeled sections → score floor of 40.</summary>
    private const int AtsSectionPenalty = 15;

    private static readonly string[] LabeledSections =
    {
        "education", "experience", "skills", "projects"
    };

    public AnalyzeResumeHandler(
        IApplicationDbContext db,
        ITextExtractor extractor,
        ICurrentUserService currentUser,
        [FromKeyedServices("ResumeAnalysis")] IRateLimiter rateLimiter,
        ILogger<AnalyzeResumeHandler> logger)
    {
        _db = db;
        _extractor = extractor;
        _currentUser = currentUser;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <exception cref="UnauthorizedAccessException">No authenticated user.</exception>
    /// <exception cref="NotFoundException">JD unknown or not owned by the caller.</exception>
    /// <exception cref="ValidationException">File extraction failed or was over limit.</exception>
    public async Task<ApiResponse<AnalyzeResumeResultDto>> Handle(
        AnalyzeResumeCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to analyze a resume.");

        // Cross-instance rate limit via Redis — fail-closed per the
        // Infrastructure contract: Redis down → error, quota never bypassed.
        // Dedicated limiter: 5 analyses per user per minute.
        await _rateLimiter.CheckAsync($"resume-analyze:{userId}", cancellationToken);

        // Unknown and foreign JDs both surface as null → 404 (no existence oracle).
        var jd = await _db.JobDescriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JdId, cancellationToken);

        if (jd is null)
            throw new NotFoundException("No job description found for the user.");

        string resumeText;
        try
        {
            resumeText = await _extractor.ExtractAsync(request.Content, ".pdf", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client disconnect or timeout — propagate as cancellation, not a
            // corrupt-file validation error.
            throw;
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Corrupt / scanned / otherwise unreadable PDF → 400, never 500.
            // Preserve the original exception so production logs retain the root cause.
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = ["Unable to read the PDF file — it may be corrupt or image-only."]
            }, ex);
        }

        if (string.IsNullOrWhiteSpace(resumeText))
        {
            // Image-only or otherwise textless PDF — nothing to match against.
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = ["Unable to read the PDF file — it may be corrupt or image-only."]
            });
        }

        var skillWeights = await _db.JdSkillWeights
            .AsNoTracking()
            .Where(sw => sw.JobDescriptionId == jd.Id)
            .ToListAsync(cancellationToken);

        // Heading detection needs line structure (raw text); skill matching
        // needs whitespace normalization so skills wrapping across PDF lines
        // still hit.
        var ats = AnalyzeAts(resumeText);
        var normalizedResume = NormalizeWhitespace(resumeText);
        var matched = new List<string>();
        var missing = new List<string>();
        double matchedWeight = 0;
        double totalWeight = 0;

        foreach (var sw in skillWeights)
        {
            totalWeight += sw.Weight;
            if (SkillAppearsInText(sw.SkillName, normalizedResume))
            {
                matched.Add(sw.SkillName);
                matchedWeight += sw.Weight;
            }
            else
            {
                missing.Add(sw.SkillName);
            }
        }

        var matchScore = totalWeight > 0
            ? (int)Math.Round(matchedWeight / totalWeight * 100)
            : 100;

        var suggestions = BuildSuggestions(missing, ats.Issues);

        var analysis = new ResumeAnalysis
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            JdId = jd.Id,
            FileName = request.FileName,
            MatchScore = matchScore,
            KeywordMatchJson = JsonSerializer.Serialize(new KeywordMatch { Matched = matched, Missing = missing }),
            AtsParseabilityJson = JsonSerializer.Serialize(ats),
            SuggestionsJson = JsonSerializer.Serialize(suggestions),
            CreatedAt = DateTime.UtcNow
        };
        _db.ResumeAnalyses.Add(analysis);
        await _db.SaveChangesAsync(cancellationToken);

        var dto = new AnalyzeResumeResultDto
        {
            AnalysisId = analysis.Id,
            MatchScore = matchScore,
            KeywordMatch = new KeywordMatch { Matched = matched, Missing = missing },
            AtsParseability = ats,
            Suggestions = suggestions
        };

        return ApiResponse<AnalyzeResumeResultDto>.SuccessResult(dto, "Resume analyzed successfully.");
    }

    private static bool SkillAppearsInText(string skill, string normalizedText)
    {
        // Whitespace-normalize the stored skill name so "System  Design" in a
        // JD still matches "System Design" in the resume. Normalize on all
        // whitespace (tabs, newlines, multiple spaces), not just literal spaces,
        // so a key like "Machine\tLearning" still matches "Machine Learning".
        var skillName = NormalizeWhitespace(skill);
        if (string.IsNullOrEmpty(skillName))
            return false;

        // Word-boundary match: "C" must not match "C++"/"C#", "Java" must not
        // match "JavaScript". `+#` are allowed adjacent so "C++" as a skill
        // still matches "C++" text.
        var pattern = $@"(?<![A-Za-z0-9+#]){Regex.Escape(skillName)}(?![A-Za-z0-9+#])";
        return Regex.IsMatch(normalizedText, pattern, RegexOptions.IgnoreCase);
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static AtsParseability AnalyzeAts(string normalizedText)
    {
        var lines = normalizedText.Split('\n');
        var missing = new List<string>();

        foreach (var section in LabeledSections)
        {
            var hasHeading = lines.Any(line =>
            {
                var trimmed = line.Trim().TrimStart('#').Trim().TrimEnd(':', '.', '-', ' ');
                return trimmed.Equals(section, StringComparison.OrdinalIgnoreCase);
            });

            if (!hasHeading)
                missing.Add(section);
        }

        var score = Math.Max(0, 100 - missing.Count * AtsSectionPenalty);
        return new AtsParseability
        {
            Score = score,
            Issues = missing.Select(m =>
                $"No '{m}' section detected — ATS parsers expect clearly labeled sections.").ToList()
        };
    }

    private static List<string> BuildSuggestions(List<string> missingSkills, List<string> atsIssues)
    {
        var suggestions = new List<string>();

        foreach (var skill in missingSkills)
        {
            suggestions.Add(
                $"Add '{skill}' to your resume — it appears in the JD but is missing from your resume.");
        }

        suggestions.AddRange(atsIssues);

        if (suggestions.Count == 0)
        {
            suggestions.Add("Strong match — keep section headings plain and skills current.");
        }

        return suggestions;
    }

}
