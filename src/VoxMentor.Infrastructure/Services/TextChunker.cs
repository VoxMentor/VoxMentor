namespace VoxMentor.Infrastructure.Services;

/// <summary>Splits raw text into overlapping word-window chunks for RAG ingestion.</summary>
public static class TextChunker
{
    // ponytail: ~0.75 words/token approximation of the 500-token/50-overlap spec —
    // no tokenizer dependency; swap in a real tokenizer only if chunk quality matters.
    public const int ChunkSizeWords = 375;
    public const int ChunkOverlapWords = 37;

    public static IReadOnlyList<string> Chunk(
        string text,
        int chunkSizeWords = ChunkSizeWords,
        int overlapWords = ChunkOverlapWords)
    {
        if (chunkSizeWords <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeWords));
        }
        if (overlapWords < 0 || overlapWords >= chunkSizeWords)
        {
            throw new ArgumentOutOfRangeException(nameof(overlapWords));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return Array.Empty<string>();
        }
        if (words.Length <= chunkSizeWords)
        {
            return new[] { string.Join(' ', words) };
        }

        var chunks = new List<string>();
        var step = chunkSizeWords - overlapWords;
        for (var start = 0; start < words.Length; start += step)
        {
            var count = Math.Min(chunkSizeWords, words.Length - start);
            chunks.Add(string.Join(' ', words, start, count));
            if (start + count >= words.Length)
            {
                break;
            }
        }
        return chunks;
    }
}
