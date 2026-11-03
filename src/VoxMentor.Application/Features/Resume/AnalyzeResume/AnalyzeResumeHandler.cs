using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Features.Resume.AnalyzeResume;

/// <summary>
/// Extracts the resume text, loads the JD's weighted skills, and produces a
/// matched/missing report plus an ATS parseability score and suggestions.
/// </summary>
public sealed class AnalyzeResumeHandler : IRequestHandler<AnalyzeResumeCommand, ApiResponse<AnalyzeResumeResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ITextExtractor _extractor;

    private static readonly string[] CanonicalSections = { "education", "experience", "skills", "projects" };

    public AnalyzeResumeHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ITextExtractor extractor)
    {
        _db = db;
        _currentUser = currentUser;
        _extractor = extractor;
    }

    public async Task<ApiResponse<AnalyzeResumeResultDto>> Handle(
        AnalyzeResumeCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to analyze a resume.");

        // Foreign and unknown JDs both surface as null -> 404 (no existence oracle).
        var jd = await _db.JobDescriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JdId, cancellationToken);
        if (jd is null || jd.UserId != userId)
            throw new NotFoundException("No job description found for the user.");

        var extension = Path.GetExtension(request.FileName)?.ToLowerInvariant() ?? string.Empty;
        string text;
        try
        {
            text = await _extractor.ExtractAsync(request.Content, extension, cancellationToken);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ValidationException))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[] { "Could not read the resume file (corrupt or unreadable PDF)." }
            });
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[] { "No extractable text found in the resume (scanned or image-only PDF?)." }
            });

        var weights = await _db.JdSkillWeights
            .AsNoTracking()
            .Where(sw => sw.JobDescriptionId == jd.Id)
            .ToListAsync(cancellationToken);

        // Match against whitespace-normalized text so skills that wrap across
        // PDF lines ("System Design") still hit.
        var haystack = Regex.Replace(text, @"\s+", " ");
        var matched = new List<string>();
        var missing = new List<string>();
        double totalWeight = 0;
        double matchedWeight = 0;
        foreach (var sw in weights)
        {
            totalWeight += sw.Weight;
            if (SkillAppearsInText(sw.SkillName, haystack))
            {
                matched.Add(sw.SkillName);
                matchedWeight += sw.Weight;
            }
            else
            {
                missing.Add(sw.SkillName);
            }
        }

        var score = totalWeight > 0
            ? (int)Math.Round(100.0 * matchedWeight / totalWeight)
            : 0;

        // ATS parseability heuristics: labeled sections.
        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        var issues = new List<string>();
        foreach (var section in CanonicalSections)
        {
            // Match heading-shaped lines, not the word appearing in prose:
            // "Education and experience gave me skills..." must not count.
            var headingPattern = $@"^\s*#*\s*{Regex.Escape(section)}\s*[:\-–—]*\s*$";
            if (!lines.Any(line => Regex.IsMatch(line, headingPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                issues.Add($"No '{section}' section detected — ATS parsers expect clearly labeled sections.");
        }
        var atsScore = Math.Max(0, 100 - 15 * issues.Count);

        var suggestions = new List<string>();
        foreach (var skill in missing)
            suggestions.Add($"Add '{skill}' to your resume — it appears in the JD but is missing from your resume.");
        if (issues.Count > 0)
            suggestions.Add("Use plain, clearly-labeled section headings (Education, Experience, Skills, Projects) instead of tables or columns.");

        return ApiResponse<AnalyzeResumeResultDto>.SuccessResult(
            new AnalyzeResumeResultDto
            {
                MatchScore = score,
                KeywordMatch = new ResumeKeywordMatchDto { Matched = matched, Missing = missing },
                AtsParseability = new ResumeAtsParseabilityDto { Score = atsScore, Issues = issues },
                Suggestions = suggestions
            },
            "Resume analyzed successfully.");
    }

    /// <summary>
    /// Case-insensitive word-boundary match. Boundary is alphanumerics so that
    /// "Java" does not match "JavaScript"; trailing "+" and "#" keep single-letter
    /// "C" from matching "C++"/"C#" text while full skills like "C#" still match.
    /// Stored skill names are whitespace-normalized so "System  Design" matches.
    /// ponytail: ASCII boundary only — single-letter skills can still false-positive
    /// inside "C++"/"C#"-style tokens; restrict or document if such skills are added.
    /// </summary>
    private static bool SkillAppearsInText(string skill, string text)
    {
        if (string.IsNullOrWhiteSpace(skill)) return false;
        var normalizedSkill = Regex.Replace(skill.Trim(), @"\s+", " ");
        return Regex.IsMatch(text, $@"(?<![A-Za-z0-9]){Regex.Escape(normalizedSkill)}(?![A-Za-z0-9+#])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
