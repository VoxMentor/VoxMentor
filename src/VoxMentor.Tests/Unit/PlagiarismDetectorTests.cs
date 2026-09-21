using VoxMentor.Infrastructure.Plagiarism;

namespace VoxMentor.Tests.Unit;

public class PlagiarismDetectorTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var a = new float[] { 1, 0, 0, 1 };
        var b = new float[] { 1, 0, 0, 1 };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(1f, result, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1, 0 };
        var b = new float[] { 0, 1 };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(0f, result, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        var a = new float[] { 1, 0 };
        var b = new float[] { -1, 0 };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(-1f, result, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_SimilarVectors_ReturnsHighScore()
    {
        var a = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        var b = new float[] { 0.11f, 0.21f, 0.29f, 0.41f, 0.49f };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.True(result > 0.99f, $"Expected > 0.99 but got {result}");
    }

    [Fact]
    public void CosineSimilarity_DifferentVectors_ReturnsLowerScore()
    {
        var a = new float[] { 1, 0, 0 };
        var b = new float[] { 0, 1, 0 };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(0f, result, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_DimensionMismatch_ThrowsArgumentException()
    {
        var a = new float[] { 1, 0 };
        var b = new float[] { 1, 0, 0 };

        Assert.Throws<ArgumentException>(() => PlagiarismDetector.CosineSimilarity(a, b));
    }

    [Fact]
    public void CosineSimilarity_AllZeroVectors_ReturnsZero()
    {
        var a = new float[] { 0, 0, 0 };
        var b = new float[] { 0, 0, 0 };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(0f, result, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_ScaledVectors_ReturnsOne()
    {
        var a = new float[] { 1, 2, 3 };
        var b = new float[] { 2, 4, 6 }; // same direction, 2x scale

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(1f, result, precision: 4);
    }
}
