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

    [Fact]
    public void EnsureUniquePath_StripsExistingSuffix_BeforeAppendingNew()
    {
        // Simulates the forget/re-organize bug: file already has " (1)" in the name
        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.FileExists("/tv/Show/Episode (1).mkv")).Returns(true);
        // After stripping " (1)", base becomes "Episode", so candidates start at "Episode (1)"
        // which exists, then "Episode (2)" which does not
        fs.Setup(f => f.FileExists("/tv/Show/Episode (2).mkv")).Returns(false);

        var result = PathHelpers.EnsureUniquePath("/tv/Show/Episode (1).mkv", fs.Object);

        Assert.Equal("/tv/Show/Episode (2).mkv", result);
    }

    [Fact]
    public void EnsureUniquePath_StripsMultipleStackedSuffixes()
    {
        // File has accumulated " (1) (1) (1)" from repeated forget/re-organize cycles
        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.FileExists("/tv/Show/Episode (1) (1) (1).mkv")).Returns(true);
        // After stripping, base becomes "Episode", candidate "Episode (1)" is checked
        fs.Setup(f => f.FileExists("/tv/Show/Episode (1).mkv")).Returns(false);

        var result = PathHelpers.EnsureUniquePath("/tv/Show/Episode (1) (1) (1).mkv", fs.Object);

        Assert.Equal("/tv/Show/Episode (1).mkv", result);
    }

    [Theory]
    [InlineData("Movie (1)", "Movie")]
    [InlineData("Movie (1) (1) (1)", "Movie")]
    [InlineData("Movie (2)", "Movie")]
    [InlineData("[SubsPlease] Frieren S2 - 03 (1080p) [7556A22B] (1) (1) (1)", "[SubsPlease] Frieren S2 - 03 (1080p) [7556A22B]")]
    [InlineData("Movie", "Movie")]
    [InlineData("Movie (1080p)", "Movie (1080p)")]
    [InlineData("File (10)", "File")]
    public void StripTrailingCopySuffixes_RemovesOnlyTrailingCopySuffixes(string input, string expected)
    {
        var result = PathHelpers.StripTrailingCopySuffixes(input);
        Assert.Equal(expected, result);
    }
}
