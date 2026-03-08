using MediaOrganizer.Helpers;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class PathHelpersTests
{
    [Fact]
    public void EnsureUniquePath_ReturnsOriginalWhenNoConflict()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.FileExists("/media/Movie/Movie.mp4")).Returns(false);

        var result = PathHelpers.EnsureUniquePath("/media/Movie/Movie.mp4", fs.Object);

        Assert.Equal("/media/Movie/Movie.mp4", result);
    }

    [Fact]
    public void EnsureUniquePath_AppendsNumericSuffix_WhenFileExists()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.FileExists("/media/Movie/Movie.mp4")).Returns(true);
        fs.Setup(f => f.FileExists("/media/Movie/Movie (1).mp4")).Returns(false);

        var result = PathHelpers.EnsureUniquePath("/media/Movie/Movie.mp4", fs.Object);

        Assert.Equal("/media/Movie/Movie (1).mp4", result);
    }

    [Fact]
    public void EnsureUniquePath_IncrementsUntilUnique()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.FileExists("/media/Movie/Movie.mp4")).Returns(true);
        fs.Setup(f => f.FileExists("/media/Movie/Movie (1).mp4")).Returns(true);
        fs.Setup(f => f.FileExists("/media/Movie/Movie (2).mp4")).Returns(true);
        fs.Setup(f => f.FileExists("/media/Movie/Movie (3).mp4")).Returns(false);

        var result = PathHelpers.EnsureUniquePath("/media/Movie/Movie.mp4", fs.Object);

        Assert.Equal("/media/Movie/Movie (3).mp4", result);
    }

    [Fact]
    public void EnsureUniquePath_PreservesExtension()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.FileExists("/tv/Show/Episode.mkv")).Returns(true);
        fs.Setup(f => f.FileExists("/tv/Show/Episode (1).mkv")).Returns(false);

        var result = PathHelpers.EnsureUniquePath("/tv/Show/Episode.mkv", fs.Object);

        Assert.EndsWith(".mkv", result);
        Assert.Contains("(1)", result);
    }
}
