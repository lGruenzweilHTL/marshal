using Marshal.Memory.Browsing;

namespace Marshal.Memory.Tests.Browsing;

public sealed class PrincipalComponentsTests
{
    [Fact]
    public void Project_TwoClusters_SeparatedOnTheFirstAxis()
    {
        float[][] vectors =
        [
            [1, 0, 0, 0.1f], [0.9f, 0.1f, 0, 0], [1, 0, 0.1f, 0],
            [0, 0, 1, 0.1f], [0.1f, 0, 0.9f, 0], [0, 0.1f, 1, 0],
        ];

        IReadOnlyList<(double X, double Y)> points = PrincipalComponents.Project(vectors);

        double[] first = [.. points.Take(3).Select(point => point.X)];
        double[] second = [.. points.Skip(3).Select(point => point.X)];
        Assert.True(first.All(x => x > 0) && second.All(x => x < 0) || first.All(x => x < 0) && second.All(x => x > 0));
        Assert.True(Math.Abs(first.Average() - second.Average()) > 1);
    }

    [Fact]
    public void Project_Deterministic()
    {
        float[][] vectors = [[1, 2, 3], [3, 1, 2], [2, 3, 1], [0, 0, 1]];

        Assert.Equal(PrincipalComponents.Project(vectors), PrincipalComponents.Project(vectors));
    }

    [Fact]
    public void Project_SecondAxisOrthogonalSpread()
    {
        // Spread along x is largest, along y second: the projection keeps both.
        float[][] vectors = [[4, 0, 0], [-4, 0, 0], [0, 1, 0], [0, -1, 0]];

        IReadOnlyList<(double X, double Y)> points = PrincipalComponents.Project(vectors);

        Assert.Equal(4, Math.Abs(points[0].X), 3);
        Assert.Equal(0, points[0].Y, 3);
        Assert.Equal(1, Math.Abs(points[2].Y), 3);
        Assert.Equal(0, points[2].X, 3);
    }

    [Fact]
    public void Project_FewerThanTwoVectors_Origin()
    {
        Assert.Empty(PrincipalComponents.Project([]));
        Assert.Equal([(0d, 0d)], PrincipalComponents.Project([[1, 2, 3]]));
    }
}
