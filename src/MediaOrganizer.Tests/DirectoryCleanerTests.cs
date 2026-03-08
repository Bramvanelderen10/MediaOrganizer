using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class DirectoryCleanerTests
{
    [Fact]
    public void CleanupDirectoriesWithoutMedia_DoesNotDeleteParentWhenChildHasVideo()
    {
        var root = CreateTempDirectory();
        try
        {
            var parent = Path.Combine(root, "Show");
            var child = Path.Combine(parent, "Season 01");
            Directory.CreateDirectory(child);

            // Parent has a non-media file, child has a media file.
            File.WriteAllText(Path.Combine(parent, "note.txt"), "keep this because parent must stay");
            File.WriteAllText(Path.Combine(child, "Episode 01.mkv"), "video");

            var logger = Mock.Of<ILogger<DirectoryCleaner>>();
            var sut = new DirectoryCleaner(logger);

            var deletedFiles = sut.CleanupDirectoriesWithoutMedia(
                root,
                videoExtensions: new[] { ".mkv" },
                subtitleExtensions: new[] { ".srt" });

            Assert.True(Directory.Exists(parent));
            Assert.True(Directory.Exists(child));
            Assert.True(File.Exists(Path.Combine(child, "Episode 01.mkv")));
            Assert.True(File.Exists(Path.Combine(parent, "note.txt")));
            Assert.Equal(0, deletedFiles);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void CleanupDirectoriesWithoutMedia_DeletesDirectoriesWithNoVideoOrSubtitle()
    {
        var root = CreateTempDirectory();
        try
        {
            // D: should be deleted (only junk)
            var d = Path.Combine(root, "D");
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "junk.nfo"), "junk");

            // C/child: should be deleted (no media anywhere)
            var c = Path.Combine(root, "C");
            var cChild = Path.Combine(c, "child");
            Directory.CreateDirectory(cChild);
            File.WriteAllText(Path.Combine(cChild, "readme.txt"), "junk");

            // E/child: should remain due to subtitle
            var e = Path.Combine(root, "E");
            var eChild = Path.Combine(e, "child");
            Directory.CreateDirectory(eChild);
            File.WriteAllText(Path.Combine(eChild, "subtitles.SRT"), "subtitle");

            var logger = Mock.Of<ILogger<DirectoryCleaner>>();
            var sut = new DirectoryCleaner(logger);

            var deletedFiles = sut.CleanupDirectoriesWithoutMedia(
                root,
                videoExtensions: new[] { ".mp4", ".mkv" },
                subtitleExtensions: new[] { ".srt" });

            Assert.False(Directory.Exists(d));
            Assert.False(Directory.Exists(c));
            Assert.True(Directory.Exists(e));
            Assert.True(Directory.Exists(eChild));
            Assert.True(File.Exists(Path.Combine(eChild, "subtitles.SRT")));

            Assert.True(deletedFiles >= 2);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "MediaOrganizerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
