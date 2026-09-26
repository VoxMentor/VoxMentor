using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>Coverage for the word-window chunker used by textbook ingestion (#70).</summary>
public class TextChunkerTests
{
    private static string WordSoup(int count)
        => string.Join(' ', Enumerable.Range(0, count).Select(i => $"w{i}"));

    [Fact]
    public void Chunk_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(TextChunker.Chunk(""));
        Assert.Empty(TextChunker.Chunk("   \n\t  "));
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var chunks = TextChunker.Chunk(WordSoup(50));

        var chunk = Assert.Single(chunks);
        Assert.Equal(WordSoup(50), chunk);
    }

    [Fact]
    public void Chunk_LongText_EveryChunkWithinSizeLimit()
    {
        var chunks = TextChunker.Chunk(WordSoup(1000));

        Assert.All(chunks, c =>
            Assert.True(c.Split(' ').Length <= TextChunker.ChunkSizeWords,
                $"chunk exceeds {TextChunker.ChunkSizeWords} words"));
        Assert.Equal(3, chunks.Count);
    }

    [Fact]
    public void Chunk_ConsecutiveChunksOverlapBySpecAmount()
    {
        var words = WordSoup(1000).Split(' ');
        var chunks = TextChunker.Chunk(WordSoup(1000));

        var firstTail = words.Skip(TextChunker.ChunkSizeWords - TextChunker.ChunkOverlapWords)
            .Take(TextChunker.ChunkOverlapWords);
        var secondHead = chunks[1].Split(' ').Take(TextChunker.ChunkOverlapWords);

        Assert.Equal(firstTail, secondHead);
    }

    [Fact]
    public void Chunk_CoversAllWordsThroughEnd()
    {
        var words = WordSoup(1000).Split(' ');
        var chunks = TextChunker.Chunk(WordSoup(1000));

        var lastChunkWords = chunks[^1].Split(' ');
        Assert.Equal(words[^1], lastChunkWords[^1]);

        // last chunk still overlaps the previous one by the spec amount
        var previousTail = chunks[^2].Split(' ').TakeLast(TextChunker.ChunkOverlapWords);
        var lastHead = lastChunkWords.Take(TextChunker.ChunkOverlapWords);
        Assert.Equal(previousTail, lastHead);
    }

    [Fact]
    public void Chunk_ExactlyOneWindowSize_SingleChunk()
    {
        var chunks = TextChunker.Chunk(WordSoup(TextChunker.ChunkSizeWords));

        Assert.Single(chunks);
    }

    [Fact]
    public void Chunk_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Chunk("x", chunkSizeWords: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Chunk("x", chunkSizeWords: 10, overlapWords: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Chunk("x", chunkSizeWords: 10, overlapWords: -1));
    }

    [Fact]
    public void Chunk_LargeInput_DoesNotHang()
    {
        // ponytail: guards the loop-advance invariant — a zero step would hang CI.
        var chunks = TextChunker.Chunk(WordSoup(10_000));

        Assert.True(chunks.Count > 1);
        Assert.Equal("w9999", chunks[^1].Split(' ')[^1]);
    }
}
