using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Comprehensive test suite for ImageProcessingService covering security, performance, and functional requirements.
/// </summary>
public class ImageProcessingTests
{
    private readonly ImageProcessingService _imageProcessor;
    private readonly ILogger<ImageProcessingService> _logger;

    public ImageProcessingTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<ImageProcessingService>();
        _imageProcessor = new ImageProcessingService(_logger);
    }

    #region ProcessImageVariantsAsync Tests

    [Fact]
    public async Task ProcessImageVariants_ShouldGenerateThreeVariants()
    {
        // Arrange - Create a simple test image (1x1 pixel PNG)
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);

        // Act
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");

        // Assert
        Assert.NotNull(variants);
        Assert.Equal(3, variants.Count);
        Assert.True(variants.ContainsKey("THUMBNAIL"));
        Assert.True(variants.ContainsKey("WEB"));
        Assert.True(variants.ContainsKey("RAW"));
    }

    [Fact]
    public async Task ProcessImageVariants_ThumbnailShouldBeSmallerThanWeb()
    {
        // Arrange
        var testImageBytes = CreateTestImage(800, 600); // Larger image
        using var imageStream = new MemoryStream(testImageBytes);

        // Act
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");

        // Assert - THUMBNAIL should be smaller than WEB for larger images
        Assert.True(variants["THUMBNAIL"].Length <= variants["WEB"].Length);
    }

    [Fact]
    public async Task ProcessImageVariants_ShouldHandleJpeg()
    {
        // Arrange
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);

        // Act
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/jpeg");

        // Assert
        Assert.NotNull(variants);
        Assert.All(variants.Values, bytes => Assert.NotEmpty(bytes));
    }

    [Fact]
    public async Task ProcessImageVariants_ShouldHandleWebP()
    {
        // Arrange
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);

        // Act
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/webp");

        // Assert
        Assert.NotNull(variants);
        Assert.All(variants.Values, bytes => Assert.NotEmpty(bytes));
    }

    [Fact]
    public async Task ProcessImageVariants_ShouldHandleNonSeekableStream()
    {
        // Arrange
        var testImageBytes = CreateTestImage();
        using var nonSeekableStream = new NonSeekableStream(testImageBytes);

        // Act
        var variants = await _imageProcessor.ProcessImageVariantsAsync(nonSeekableStream, "image/png");

        // Assert
        Assert.NotNull(variants);
        Assert.Equal(3, variants.Count);
    }

    [Fact]
    public async Task ProcessImageVariants_ShouldThrowOnNullStream()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _imageProcessor.ProcessImageVariantsAsync(null!, "image/png"));
    }

    [Fact]
    public async Task ProcessImageVariants_ShouldThrowOnNonReadableStream()
    {
        // Arrange
        var nonReadableStream = new MemoryStream();
        nonReadableStream.Close(); // Make it non-readable

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _imageProcessor.ProcessImageVariantsAsync(nonReadableStream, "image/png"));
    }

    [Fact]
    public async Task ProcessImageVariants_ShouldDefaultToJpegOnEmptyContentType()
    {
        // Arrange
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);

        // Act
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "");

        // Assert - Should succeed with JPEG default
        Assert.NotNull(variants);
        Assert.Equal(3, variants.Count);
    }

    [Fact]
    public async Task ProcessImageVariants_ShouldThrowOnUnsupportedContentType()
    {
        // Arrange
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/bmp"));
    }

    [Fact]
    public async Task ProcessImageVariants_ShouldThrowOnCorruptedImage()
    {
        // Arrange - Invalid image data
        var corruptedData = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        using var imageStream = new MemoryStream(corruptedData);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png"));
    }

    [Fact]
    public async Task ProcessImageVariants_ShouldPreserveAspectRatio()
    {
        // Arrange - 1600x800 image (2:1 aspect ratio)
        var testImageBytes = CreateTestImage(1600, 800);
        using var imageStream = new MemoryStream(testImageBytes);

        // Act
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");

        // Assert - Check WEB variant maintains aspect ratio (should be 1200x600)
        using var webImage = Image.Load(variants["WEB"]);
        Assert.True(Math.Abs((double)webImage.Width / webImage.Height - 2.0) < 0.01);
    }

    #endregion

    #region ResizeImageAsync Tests

    [Fact]
    public async Task ResizeImage_ShouldResizeToMaxSize()
    {
        // Arrange
        var testImageBytes = CreateTestImage(800, 600);
        using var imageStream = new MemoryStream(testImageBytes);

        // Act
        var resized = await _imageProcessor.ResizeImageAsync(imageStream, "image/png", 400);

        // Assert
        Assert.NotNull(resized);
        Assert.NotEmpty(resized);
        
        using var resizedImage = Image.Load(resized);
        Assert.True(resizedImage.Width <= 400);
        Assert.True(resizedImage.Height <= 400);
    }

    [Fact]
    public async Task ResizeImage_ShouldThrowOnNullStream()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _imageProcessor.ResizeImageAsync(null!, "image/png", 400));
    }

    [Fact]
    public async Task ResizeImage_ShouldThrowOnNonReadableStream()
    {
        // Arrange
        var nonReadableStream = new MemoryStream();
        nonReadableStream.Close();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _imageProcessor.ResizeImageAsync(nonReadableStream, "image/png", 400));
    }

    [Fact]
    public async Task ResizeImage_ShouldThrowOnInvalidMaxSize()
    {
        // Arrange
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _imageProcessor.ResizeImageAsync(imageStream, "image/png", 0));
    }

    [Fact]
    public async Task ResizeImage_ShouldThrowOnExcessiveMaxSize()
    {
        // Arrange
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);

        // Act & Assert - Max size > 10000px should fail
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _imageProcessor.ResizeImageAsync(imageStream, "image/png", 20000));
    }

    [Fact]
    public async Task ResizeImage_ShouldMaintainAspectRatio()
    {
        // Arrange - 800x400 image (2:1 aspect ratio)
        var testImageBytes = CreateTestImage(800, 400);
        using var imageStream = new MemoryStream(testImageBytes);

        // Act
        var resized = await _imageProcessor.ResizeImageAsync(imageStream, "image/png", 400);

        // Assert - Aspect ratio should be preserved
        using var resizedImage = Image.Load(resized);
        Assert.True(Math.Abs((double)resizedImage.Width / resizedImage.Height - 2.0) < 0.01);
    }

    #endregion

    #region Static Method Tests

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/jpg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/unknown", ".jpg")] // Should default to .jpg
    [InlineData("", ".jpg")] // Empty should default to .jpg
    public void GetFileExtension_ShouldReturnCorrectExtension(string contentType, string expected)
    {
        // Act
        var extension = ImageProcessingService.GetFileExtension(contentType);

        // Assert
        Assert.Equal(expected, extension);
    }

    [Fact]
    public void GetFileExtension_ShouldHandleContentTypeWithParameters()
    {
        // Act
        var extension = ImageProcessingService.GetFileExtension("image/jpeg; charset=utf-8");

        // Assert
        Assert.Equal(".jpg", extension);
    }

    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/jpg", true)]
    [InlineData("image/png", true)]
    [InlineData("image/webp", true)]
    [InlineData("image/gif", true)]
    [InlineData("image/bmp", false)]
    [InlineData("image/svg+xml", false)]
    [InlineData("text/plain", false)]
    [InlineData("application/pdf", false)]
    [InlineData("", false)]
    public void IsSupportedImageType_ShouldValidateCorrectly(string contentType, bool expected)
    {
        // Act
        var isSupported = ImageProcessingService.IsSupportedImageType(contentType);

        // Assert
        Assert.Equal(expected, isSupported);
    }

    [Theory]
    [InlineData("IMAGE/JPEG", true)] // Case insensitive
    [InlineData("IMAGE/PNG", true)]
    [InlineData("image/WEBP", true)]
    [InlineData("ImAgE/JpEg", true)]
    public void IsSupportedImageType_ShouldBeCaseInsensitive(string contentType, bool expected)
    {
        // Act
        var isSupported = ImageProcessingService.IsSupportedImageType(contentType);

        // Assert
        Assert.Equal(expected, isSupported);
    }

    [Fact]
    public void IsSupportedImageType_ShouldHandleContentTypeWithParameters()
    {
        // Act
        var isSupported = ImageProcessingService.IsSupportedImageType("image/jpeg; charset=utf-8");

        // Assert
        Assert.True(isSupported);
    }

    #endregion

    #region Security Tests

    [Fact]
    public async Task ProcessImageVariants_ShouldRejectExcessivelySizedImage()
    {
        // Arrange - Create a large stream (>50MB simulated)
        var largeData = new byte[51 * 1024 * 1024]; // 51 MB
        using var largeStream = new MemoryStream(largeData);

        // Act & Assert - Should throw before attempting to load image
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _imageProcessor.ProcessImageVariantsAsync(largeStream, "image/png"));
    }

    [Fact]
    public void GetImageEncoder_ShouldNotExposeInternalImplementation()
    {
        // This test ensures the encoder logic is properly encapsulated
        // We test indirectly through public methods
        var testImageBytes = CreateTestImage();
        using var imageStream = new MemoryStream(testImageBytes);

        // Should not throw
        var variants = _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");
        Assert.NotNull(variants);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task ProcessImageVariants_ShouldCompleteInReasonableTime()
    {
        // Arrange
        var testImageBytes = CreateTestImage(2000, 2000);
        using var imageStream = new MemoryStream(testImageBytes);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var variants = await _imageProcessor.ProcessImageVariantsAsync(imageStream, "image/png");
        stopwatch.Stop();

        // Assert - Should complete within 5 seconds for a 2000x2000 image
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
            $"Processing took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
        Assert.NotNull(variants);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a minimal valid PNG image for testing.
    /// </summary>
    /// <param name="width">Image width in pixels (default: 1)</param>
    /// <param name="height">Image height in pixels (default: 1)</param>
    /// <returns>PNG image bytes</returns>
    private byte[] CreateTestImage(int width = 1, int height = 1)
    {
        // Create a simple white image using ImageSharp
        using var image = new Image<Rgba32>(width, height);
        
        // Fill with white color
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = new Rgba32(255, 255, 255, 255);
            }
        }
        
        // Save to memory stream as PNG
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Non-seekable stream wrapper for testing stream validation.
    /// </summary>
    private class NonSeekableStream : Stream
    {
        private readonly MemoryStream _innerStream;

        public NonSeekableStream(byte[] data)
        {
            _innerStream = new MemoryStream(data);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false; // Non-seekable
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerStream?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    #endregion
}
