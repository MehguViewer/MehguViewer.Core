using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MehguViewer.Core.Tests.Units;

public class LocalizedCoverTests : IDisposable
{
    private readonly FileBasedSeriesService _fileService;
    private readonly ImageProcessingService _imageProcessor;
    private readonly string _testDataPath;

    public LocalizedCoverTests()
    {
        // Create a temporary test directory
        _testDataPath = Path.Combine(Path.GetTempPath(), "mehguviewer_tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataPath);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Storage:DataPath", _testDataPath }
            })
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var fileServiceLogger = loggerFactory.CreateLogger<FileBasedSeriesService>();
        var imageProcessorLogger = loggerFactory.CreateLogger<ImageProcessingService>();

        _fileService = new FileBasedSeriesService(configuration, fileServiceLogger);
        _imageProcessor = new ImageProcessingService(imageProcessorLogger);
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testDataPath))
        {
            Directory.Delete(_testDataPath, true);
        }
    }

    [Fact]
    public async Task SaveCoverImageVariantsAsync_WithLanguage_ShouldSaveToLangFolder()
    {
        // Arrange
        var seriesId = "urn:mvn:series:test-123";
        var language = "ja";
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");

        // Act
        var coverUrl = await _fileService.SaveCoverImageVariantsAsync(seriesId, variants, ".png", language);

        // Assert
        Assert.NotNull(coverUrl);
        Assert.Contains("lang=ja", coverUrl);
        
        // Check that files were created in the correct location
        var expectedPath = Path.Combine(_testDataPath, "series", "test-123", "lang", "ja");
        Assert.True(Directory.Exists(expectedPath));
        Assert.True(File.Exists(Path.Combine(expectedPath, "cover-thumbnail.png")));
        Assert.True(File.Exists(Path.Combine(expectedPath, "cover-web.png")));
        Assert.True(File.Exists(Path.Combine(expectedPath, "cover-raw.png")));
    }

    [Fact]
    public async Task SaveCoverImageVariantsAsync_WithoutLanguage_ShouldSaveToDefaultFolder()
    {
        // Arrange
        var seriesId = "urn:mvn:series:test-456";
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");

        // Act
        var coverUrl = await _fileService.SaveCoverImageVariantsAsync(seriesId, variants, ".png", null);

        // Assert
        Assert.NotNull(coverUrl);
        Assert.DoesNotContain("lang=", coverUrl);
        
        // Check that files were created in the series root
        var expectedPath = Path.Combine(_testDataPath, "series", "test-456");
        Assert.True(Directory.Exists(expectedPath));
        Assert.True(File.Exists(Path.Combine(expectedPath, "cover-thumbnail.png")));
        Assert.True(File.Exists(Path.Combine(expectedPath, "cover-web.png")));
        Assert.True(File.Exists(Path.Combine(expectedPath, "cover-raw.png")));
    }

    [Fact]
    public async Task SaveCoverImageVariantsAsync_MultipleLanguages_ShouldStoreSeparately()
    {
        // Arrange
        var seriesId = "urn:mvn:series:test-789";
        var testImageBytes = CreateTestImage();
        
        // Act - Save default cover
        using (var imageStream = new MemoryStream(testImageBytes))
        {
            var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");
            await _fileService.SaveCoverImageVariantsAsync(seriesId, variants, ".png", null);
        }
        
        // Act - Save Japanese cover
        using (var imageStream = new MemoryStream(testImageBytes))
        {
            var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");
            await _fileService.SaveCoverImageVariantsAsync(seriesId, variants, ".png", "ja");
        }
        
        // Act - Save Spanish cover
        using (var imageStream = new MemoryStream(testImageBytes))
        {
            var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");
            await _fileService.SaveCoverImageVariantsAsync(seriesId, variants, ".png", "es");
        }

        // Assert - All covers should exist independently
        var seriesPath = Path.Combine(_testDataPath, "series", "test-789");
        Assert.True(File.Exists(Path.Combine(seriesPath, "cover-web.png"))); // Default
        Assert.True(File.Exists(Path.Combine(seriesPath, "lang", "ja", "cover-web.png"))); // Japanese
        Assert.True(File.Exists(Path.Combine(seriesPath, "lang", "es", "cover-web.png"))); // Spanish
    }

    [Fact]
    public async Task GetCoverImagePath_WithLanguage_ShouldReturnLocalizedCover()
    {
        // Arrange
        var seriesId = "urn:mvn:series:test-abc";
        var language = "fr";
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");
        await _fileService.SaveCoverImageVariantsAsync(seriesId, variants, ".png", language);

        // Act
        var coverPath = _fileService.GetCoverImagePath(seriesId, "web", language);

        // Assert
        Assert.NotNull(coverPath);
        Assert.Contains("lang", coverPath);
        Assert.Contains("fr", coverPath);
        Assert.Contains("cover-web", coverPath);
    }

    [Fact]
    public async Task GetCoverImagePath_WithNonExistentLanguage_ShouldFallbackToDefault()
    {
        // Arrange
        var seriesId = "urn:mvn:series:test-def";
        var testImageBytes = CreateTestImage();
        
        // Save only default cover
        using var imageStream = new MemoryStream(testImageBytes);
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");
        await _fileService.SaveCoverImageVariantsAsync(seriesId, variants, ".png", null);

        // Act - Request a language that doesn't exist
        var coverPath = _fileService.GetCoverImagePath(seriesId, "web", "de");

        // Assert - Should return default cover
        Assert.NotNull(coverPath);
        Assert.DoesNotContain("lang", coverPath);
        Assert.Contains("cover-web", coverPath);
    }

    [Fact]
    public void GetCoverImagePath_WithoutLanguage_ShouldReturnDefaultCover()
    {
        // Arrange
        var seriesId = "urn:mvn:series:test-ghi";
        
        // Create a test cover file manually
        var seriesPath = Path.Combine(_testDataPath, "series", "test-ghi");
        Directory.CreateDirectory(seriesPath);
        File.WriteAllText(Path.Combine(seriesPath, "cover-web.jpg"), "test");

        // Act
        var coverPath = _fileService.GetCoverImagePath(seriesId, "web", null);

        // Assert
        Assert.NotNull(coverPath);
        Assert.DoesNotContain("lang", coverPath);
        Assert.EndsWith("cover-web.jpg", coverPath);
    }

    [Theory]
    [InlineData("en", "cover-thumbnail.jpg")]
    [InlineData("ja", "cover-web.png")]
    [InlineData("es", "cover-raw.webp")]
    public async Task GetCoverImagePath_DifferentVariantsAndLanguages_ShouldReturnCorrectFile(
        string language, string expectedFileName)
    {
        // Arrange
        var seriesId = "urn:mvn:series:test-jkl";
        var testImageBytes = CreateTestImage();
        var variant = expectedFileName.Contains("thumbnail") ? "thumbnail" 
                    : expectedFileName.Contains("raw") ? "raw" : "web";
        var extension = Path.GetExtension(expectedFileName);
        
        using var imageStream = new MemoryStream(testImageBytes);
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, 
            extension == ".png" ? "image/png" : extension == ".webp" ? "image/webp" : "image/jpeg");
        await _fileService.SaveCoverImageVariantsAsync(seriesId, variants, extension, language);

        // Act
        var coverPath = _fileService.GetCoverImagePath(seriesId, variant, language);

        // Assert
        Assert.NotNull(coverPath);
        Assert.EndsWith(expectedFileName, coverPath);
    }

    [Fact]
    public async Task SaveCoverImageVariantsAsync_ReplacingExistingLocalizedCover_ShouldDeleteOldFiles()
    {
        // Arrange
        var seriesId = "urn:mvn:series:test-mno";
        var language = "ko";
        var testImageBytes = CreateTestImage();
        
        // Save initial cover
        using (var imageStream = new MemoryStream(testImageBytes))
        {
            var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");
            await _fileService.SaveCoverImageVariantsAsync(seriesId, variants, ".png", language);
        }
        
        var langPath = Path.Combine(_testDataPath, "series", "test-mno", "lang", "ko");
        var initialFileCount = Directory.GetFiles(langPath).Length;

        // Act - Replace with new cover in different format
        using (var imageStream = new MemoryStream(testImageBytes))
        {
            var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/jpeg");
            await _fileService.SaveCoverImageVariantsAsync(seriesId, variants, ".jpg", language);
        }

        // Assert - Old PNG files should be deleted, only JPG files remain
        var files = Directory.GetFiles(langPath);
        Assert.Equal(initialFileCount, files.Length); // Same number of variants
        Assert.All(files, f => Assert.EndsWith(".jpg", f)); // All are JPG now
        Assert.DoesNotContain(files, f => f.EndsWith(".png")); // No PNG files
    }

    /// <summary>
    /// Creates a minimal valid PNG image (1x1 white pixel) for testing.
    /// </summary>
    private byte[] CreateTestImage()
    {
        return new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        };
    }

    [Fact]
    public void LocalizedMetadata_WithPoster_ShouldStoreCorrectly()
    {
        // Arrange
        var poster = new MehguViewer.Core.Shared.Poster("/api/v1/series/test/cover?variant=web&lang=ja", "Japanese cover");
        var localizedMeta = new MehguViewer.Core.Shared.LocalizedMetadata(
            title: "テストシリーズ",
            description: "テスト説明",
            poster: poster
        );

        // Assert
        Assert.NotNull(localizedMeta.poster);
        Assert.Equal("/api/v1/series/test/cover?variant=web&lang=ja", localizedMeta.poster.url);
        Assert.Equal("Japanese cover", localizedMeta.poster.alt_text);
    }
}
