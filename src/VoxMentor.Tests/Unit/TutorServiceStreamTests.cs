using System.Runtime.CompilerServices;
using System.Text;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Coverage for the tutor streaming pipeline (#73): Ollama NDJSON line
/// parsing, stream reading (including mid-stream error objects), and the
/// excerpt prompt template from issue #72.
/// </summary>
public class TutorServiceStreamTests
{
    [Fact]
    public void ParseLine_ContentLine_ReturnsTextChunk()
    {
        var chunk = TutorService.ParseLine(
            """{"message":{"role":"assistant","content":"Hel"},"done":false}""");

        Assert.NotNull(chunk);
        Assert.Equal("Hel", chunk!.Text);
        Assert.False(chunk.IsFinal);
        Assert.Null(chunk.EvalCount);
    }

    [Fact]
    public void ParseLine_FinalLine_CarriesEvalCount()
    {
        var chunk = TutorService.ParseLine(
            """{"message":{"role":"assistant","content":""},"done":true,"eval_count":42}""");

        Assert.NotNull(chunk);
        Assert.True(chunk!.IsFinal);
        Assert.Equal(42, chunk.EvalCount);
    }

    [Fact]
    public void ParseLine_BlankLine_ReturnsNull()
    {
        Assert.Null(TutorService.ParseLine(""));
        Assert.Null(TutorService.ParseLine("   "));
    }

    [Fact]
    public void ParseLine_ErrorObject_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => TutorService.ParseLine("""{"error":"model not found"}"""));
        Assert.Equal("Tutor service temporarily unavailable.", ex.Message);
    }

    [Fact]
    public void ParseLine_InvalidJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => TutorService.ParseLine("{nope"));
    }

    [Fact]
    public async Task ReadStream_MultipleLines_YieldsChunksInOrder()
    {
        var ndjson = """
            {"message":{"role":"assistant","content":"The"},"done":false}
            {"message":{"role":"assistant","content":" sky"},"done":false}
            {"message":{"role":"assistant","content":" is blue"},"done":true,"eval_count":7}

            """;

        var chunks = new List<TutorChunk>();
        await foreach (var chunk in TutorService.ReadStreamAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(ndjson))))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
        Assert.Equal("The sky is blue", string.Concat(chunks.Select(c => c.Text)));
        Assert.True(chunks.Last().IsFinal);
        Assert.Equal(7, chunks.Last().EvalCount);
    }

    [Fact]
    public async Task ReadStream_ErrorMidStream_Throws()
    {
        var ndjson = """
            {"message":{"role":"assistant","content":"partial"},"done":false}
            {"error":"an error was encountered while running the model"}

            """;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in TutorService.ReadStreamAsync(
                new MemoryStream(Encoding.UTF8.GetBytes(ndjson))))
            {
            }
        });
        Assert.Equal("Tutor service temporarily unavailable.", ex.Message);
    }

    [Fact]
    public void BuildPrompt_IncludesExcerptsSourcesAndQuestion()
    {
        var chunks = new List<TutorService.RetrievedChunk>
        {
            new(Guid.NewGuid(), "Kadane scans once.", "algorithms.pdf"),
            new(Guid.NewGuid(), "Track max ending here.", "notes.md")
        };

        var prompt = TutorService.BuildPrompt("Why is Kadane O(n)?", chunks);

        Assert.Contains("Kadane scans once. [Source: algorithms.pdf]", prompt);
        Assert.Contains("Track max ending here. [Source: notes.md]", prompt);
        Assert.Contains("Question: Why is Kadane O(n)?", prompt);
        Assert.EndsWith("Answer:", prompt);
    }

    [Fact]
    public void BuildPrompt_NoChunks_StillAsksQuestion()
    {
        var prompt = TutorService.BuildPrompt("Hi", new List<TutorService.RetrievedChunk>());

        Assert.DoesNotContain("Excerpts:", prompt);
        Assert.Contains("Question: Hi", prompt);
    }
}
