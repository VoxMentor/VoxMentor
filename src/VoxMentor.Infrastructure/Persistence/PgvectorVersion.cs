namespace VoxMentor.Infrastructure.Persistence;

// CodeRabbit #96: hnsw.iterative_scan needs pgvector >= 0.8. PostgreSQL accepts
// unknown two-part GUCs as placeholders, so an older version silently ignores
// the SET instead of erroring — gate on pg_extension.extversion instead.
public static class PgvectorVersion
{
    // FormattableString overload of SqlQuery<T>; no interpolation holes, so EF
    // emits it verbatim. Scalar output must be aliased "Value" or composition
    // (FirstOrDefaultAsync) throws.
    public static FormattableString Sql =>
        $"SELECT extversion AS \"Value\" FROM pg_extension WHERE extname = 'vector'";

    public static bool IsSupported(string? extversion)
    {
        if (string.IsNullOrWhiteSpace(extversion)) return false;
        var parts = extversion.Split('.');
        var major = int.TryParse(parts[0], out var m) ? m : -1;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var mi) ? mi : -1;
        return major > 0 || (major == 0 && minor >= 8);
    }
}
