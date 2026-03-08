using MediaOrganizer.Discovery;
using MediaOrganizer.Helpers;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class VideoFileFinderTests
{
    private readonly Mock<IFileSystem> _fsMock = new();

    [Fact]
    public void GetVideoFiles_FiltersToAllowedExtensions()
    {
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.AllDirectories))
            .Returns(new[]
            {
                "/source/movie.mp4",
                "/source/readme.txt",
                "/source/show.mkv",
                "/source/photo.jpg",
                "/source/clip.avi",
            });

        var sut = new VideoFileFinder(_fsMock.Object);
        var result = sut.GetVideoFiles("/source", new[] { ".mp4", ".mkv", ".avi" });

        Assert.Equal(3, result.Count);
        Assert.Contains("/source/movie.mp4", result);
        Assert.Contains("/source/show.mkv", result);
        Assert.Contains("/source/clip.avi", result);
        Assert.DoesNotContain("/source/readme.txt", result);
        Assert.DoesNotContain("/source/photo.jpg", result);
    }

    [Fact]
    public void GetVideoFiles_DeletesHiddenTrashFiles()
    {
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.AllDirectories))
            .Returns(new[]
            {
                "/source/.Trash-1000-movie.mp4",
                "/source/legit.mp4",
            });

        var sut = new VideoFileFinder(_fsMock.Object);
        var result = sut.GetVideoFiles("/source", new[] { ".mp4" });

        Assert.Single(result);
        Assert.Equal("/source/legit.mp4", result[0]);
        _fsMock.Verify(f => f.DeleteFile("/source/.Trash-1000-movie.mp4"), Times.Once);
    }

    [Fact]
    public void GetVideoFiles_DoesNotDeleteNonTrashHiddenFiles()
    {
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.AllDirectories))
            .Returns(new[]
            {
                "/source/.config",
                "/source/legit.mp4",
            });

        var sut = new VideoFileFinder(_fsMock.Object);
        var result = sut.GetVideoFiles("/source", new[] { ".mp4" });

        Assert.Single(result);
        _fsMock.Verify(f => f.DeleteFile(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void GetVideoFiles_NormalizesExtensionCaseAndLeadingDot()
    {
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.AllDirectories))
            .Returns(new[]
            {
                "/source/movie.MKV",
                "/source/show.Mkv",
            });

        var sut = new VideoFileFinder(_fsMock.Object);
        // Extensions without leading dot and different casing
        var result = sut.GetVideoFiles("/source", new[] { "mkv" });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetVideoFiles_ReturnsEmpty_WhenNoMatchingFiles()
    {
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.AllDirectories))
            .Returns(new[] { "/source/readme.txt", "/source/image.png" });

        var sut = new VideoFileFinder(_fsMock.Object);
        var result = sut.GetVideoFiles("/source", new[] { ".mp4", ".mkv" });

        Assert.Empty(result);
    }
}
