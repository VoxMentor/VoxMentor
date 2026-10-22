using System.Security.Cryptography;
using System.Text;

namespace VoxMentor.Application.Common;

/// <summary>
/// Deterministic per-student question instance ids (#120). The same
/// (user, question, seed) triple always yields the same id, so a re-fetch
/// returns the same instance; different students get different ids.
/// </summary>
public static class IsomorphicInstance
{
    /// <summary>
    /// Instance id = SHA-256 of "userId:questionId:variantSeed", truncated to
    /// 16 bytes and reinterpreted as a Guid. A null seed hashes as empty
    /// string — an unseeded question still gets a stable per-student id.
    /// </summary>
    public static Guid Id(string userId, Guid questionId, string? variantSeed)
    {
        var bytes = Encoding.UTF8.GetBytes($"{userId}:{questionId}:{variantSeed ?? string.Empty}");
        var hash = SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }
}
