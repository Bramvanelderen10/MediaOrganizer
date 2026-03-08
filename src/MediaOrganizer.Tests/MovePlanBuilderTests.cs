using MediaOrganizer.MoveHistory;
using MediaOrganizer.MovePlan;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class MovePlanBuilderTests : IDisposable
{
    private readonly Mock<ILogger<MovePlanBuilder>> _loggerMock;
    private readonly Mock<IDbContextFactory<MoveHistoryDbContext>> _contextFactoryMock;
    private readonly MediaFileKeyGenerator _keyGeneratorMock;
    private readonly MoveHistoryDbContext _context;
    private MovePlanBuilder _sut;

    public MovePlanBuilderTests()
    {
        _loggerMock = new Mock<ILogger<MovePlanBuilder>>();
        _contextFactoryMock = new Mock<IDbContextFactory<MoveHistoryDbContext>>();
        _keyGeneratorMock = new MediaFileKeyGenerator();

        var options = new DbContextOptionsBuilder<MoveHistoryDbContext>()
            .UseInMemoryDatabase(databaseName: $"MoveHistoryDb_{Guid.NewGuid()}")
            .Options;
        _context = new MoveHistoryDbContext(options);

        _contextFactoryMock
            .Setup(f => f.CreateDbContext())
            .Returns(_context);

        _sut = new MovePlanBuilder(_loggerMock.Object, _contextFactoryMock.Object, _keyGeneratorMock);
    }

    public void Dispose()
    {
        _context?.Dispose();
    }

    [Fact]
    public void BuildMovePlan_NewFile_CreatesHistoryEntry()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var uniqueKey = "unique-key-123";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        var entry = _context.MoveHistory.FirstOrDefault(e => e.UniqueKey == uniqueKey && e.OriginalFilePath == testFile);
        Assert.NotNull(entry);
        Assert.False(entry.IsMoved);
    }

    [Fact]
    public void BuildMovePlan_FileDoesNotExist_SkipsFile()
    {
        // Arrange
        var testFile = "/media/nonexistent.mp4";
        var rootFolder = "/organized";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        Assert.Empty(_context.MoveHistory);
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("File no longer exists")),
                It.IsAny<Exception>(),
                It.IsAny<System.Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void BuildMovePlan_AlreadyMovedSuccessfully_IgnoresFile()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var destinationPath = "/organized/Test Movie/Test Movie.mp4";
        var uniqueKey = "unique-key-123";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        var existingEntry = new MoveHistoryEntry
        {
            UniqueKey = uniqueKey,
            OriginalFilePath = testFile,
            TargetFilePath = destinationPath,
            IsMoved = true
        };

        _context.MoveHistory.Add(existingEntry);
        _context.SaveChanges();

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        Assert.Single(_context.MoveHistory);
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("already moved successfully")),
                It.IsAny<Exception>(),
                It.IsAny<System.Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void BuildMovePlan_UnmovedFileWithSameDestination_KeepsInDatabase()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var destinationPath = "/organized/Test Movie/Test Movie.mp4";
        var uniqueKey = "unique-key-123";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        var existingEntry = new MoveHistoryEntry
        {
            UniqueKey = uniqueKey,
            OriginalFilePath = testFile,
            TargetFilePath = destinationPath,
            IsMoved = false
        };

        _context.MoveHistory.Add(existingEntry);
        _context.SaveChanges();


        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        Assert.Single(_context.MoveHistory);
    }

    [Fact]
    public void BuildMovePlan_DestinationPathChanged_CreatesNewRecord()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var oldDestination = "/old/Test Movie/Test Movie.mp4";
        var newDestination = "/organized/Test Movie/Test Movie.mp4";
        var uniqueKey = "unique-key-123";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        var oldEntry = new MoveHistoryEntry
        {
            UniqueKey = uniqueKey,
            OriginalFilePath = testFile,
            TargetFilePath = oldDestination,
            IsMoved = false
        };

        _context.MoveHistory.Add(oldEntry);
        _context.SaveChanges();

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        var newEntry = _context.MoveHistory.FirstOrDefault(e =>
            e.UniqueKey == uniqueKey &&
            e.TargetFilePath == newDestination &&
            e.IsMoved == false);
        Assert.NotNull(newEntry);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Destination path changed")),
                It.IsAny<Exception>(),
                It.IsAny<System.Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void BuildMovePlan_ShowWithMultipleEpisodes_CreatesRecordsForEachEpisode()
    {
        // Arrange
        var testFile1 = "/media/Show.S01E01.mkv";
        var testFile2 = "/media/Show.S01E02.mkv";
        var rootFolder = "/organized";

        var season = new Season(1, new List<string> { testFile1, testFile2 });
        var mediaObject = new MediaObject
        {
            Name = "Test Show",
            Type = MediaType.Show,
            Seasons = [season]
        };

        var mediaObjects = new List<MediaObject> { mediaObject };


        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        Assert.Equal(2, _context.MoveHistory.Count());
    }

    [Fact]
    public void BuildMovePlan_MovieWithCorrectDestination_CreatesCorrectPath()
    {
        // Arrange
        var testFile = "/media/Inception.2010.mp4";
        var rootFolder = "/organized";
        var expectedDestination = "/organized/Inception 2010/Inception 2010.mp4";
        var uniqueKey = "unique-key-123";

        var mediaObject = new MediaObject
        {
            Name = "Inception 2010",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        var entry = _context.MoveHistory.FirstOrDefault(e =>
            e.TargetFilePath == expectedDestination);
        Assert.NotNull(entry);
    }

    [Fact]
    public void BuildMovePlan_ShowEpisodeWithCorrectDestination_CreatesCorrectPath()
    {
        // Arrange
        var testFile = "/media/Breaking.Bad.S02E05.mkv";
        var rootFolder = "/organized";
        var expectedDestination = "/organized/Breaking Bad/Season 02/Breaking.Bad.S02E05.mkv";
        var uniqueKey = "unique-key-456";

        var season = new Season(2, new List<string> { testFile });
        var mediaObject = new MediaObject
        {
            Name = "Breaking Bad",
            Type = MediaType.Show,
            Seasons = [season]
        };

        var mediaObjects = new List<MediaObject> { mediaObject };


        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        var entry = _context.MoveHistory.FirstOrDefault(e =>
            e.TargetFilePath == expectedDestination);
        Assert.NotNull(entry);
    }

    [Fact]
    public void BuildMovePlan_EmptyMediaObjectList_SavesChangesWithoutAddingRecords()
    {
        // Arrange
        var mediaObjects = new List<MediaObject>();

        // Act
        _sut.BuildMovePlan(mediaObjects, "/organized");

        // Assert
        Assert.Empty(_context.MoveHistory);
    }

    [Fact]
    public void BuildMovePlan_MultipleMediaObjects_ProcessesAll()
    {
        // Arrange
        var movieFile = "/media/Movie.mp4";
        var showFile1 = "/media/Show.S01E01.mkv";
        var showFile2 = "/media/Show.S01E02.mkv";
        var rootFolder = "/organized";

        var movieObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = movieFile
        };

        var season = new Season(1, new List<string> { showFile1, showFile2 });
        var showObject = new MediaObject
        {
            Name = "Test Show",
            Type = MediaType.Show,
            Seasons = [season]
        };

        var mediaObjects = new List<MediaObject> { movieObject, showObject };


        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        Assert.Equal(3, _context.MoveHistory.Count());
    }
}
