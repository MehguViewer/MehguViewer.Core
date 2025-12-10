using Xunit;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using Microsoft.Extensions.Logging.Abstractions;

namespace MehguViewer.Core.Tests.Units;

/// <summary>
/// Tests for edit permission management on series and units.
/// </summary>
public class EditPermissionTests
{
    private static MemoryRepository CreateTestRepository()
    {
        var logger = new NullLogger<MemoryRepository>();
        var metadataLogger = new NullLogger<MetadataAggregationService>();
        var metadataService = new MetadataAggregationService(metadataLogger);
        return new MemoryRepository(logger, metadataService);
    }

    [Fact]
    public void GrantEditPermission_AddsUserToAllowedEditors_ForSeries()
    {
        // Arrange
        var repo = CreateTestRepository();
        var series = CreateTestSeries();
        repo.AddSeries(series);
        var userUrn = "urn:mvn:user:editor1";
        var grantedBy = "urn:mvn:user:owner";

        // Act
        repo.GrantEditPermission(series.id, userUrn, grantedBy);

        // Assert
        Assert.True(repo.HasEditPermission(series.id, userUrn));
        var permissions = repo.GetEditPermissions(series.id);
        Assert.Contains(userUrn, permissions);
        
        // Verify series object is updated
        var updatedSeries = repo.GetSeries(series.id);
        Assert.NotNull(updatedSeries?.allowed_editors);
        Assert.Contains(userUrn, updatedSeries.allowed_editors);
    }

    [Fact]
    public void RevokeEditPermission_RemovesUserFromAllowedEditors_ForSeries()
    {
        // Arrange
        var repo = CreateTestRepository();
        var series = CreateTestSeries();
        repo.AddSeries(series);
        var userUrn = "urn:mvn:user:editor1";
        var grantedBy = "urn:mvn:user:owner";
        repo.GrantEditPermission(series.id, userUrn, grantedBy);

        // Act
        repo.RevokeEditPermission(series.id, userUrn);

        // Assert
        Assert.False(repo.HasEditPermission(series.id, userUrn));
        var permissions = repo.GetEditPermissions(series.id);
        Assert.DoesNotContain(userUrn, permissions);
        
        // Verify series object is updated
        var updatedSeries = repo.GetSeries(series.id);
        Assert.DoesNotContain(userUrn, updatedSeries?.allowed_editors ?? []);
    }

    [Fact]
    public void HasEditPermission_ReturnsTrueForOwner()
    {
        // Arrange
        var repo = CreateTestRepository();
        var ownerUrn = "urn:mvn:user:owner";
        var series = CreateTestSeries(ownerUrn);
        repo.AddSeries(series);

        // Act & Assert
        Assert.True(repo.HasEditPermission(series.id, ownerUrn));
    }

    [Fact]
    public void HasEditPermission_ReturnsFalseForNonOwnerWithoutPermission()
    {
        // Arrange
        var repo = CreateTestRepository();
        var series = CreateTestSeries();
        repo.AddSeries(series);
        var otherUserUrn = "urn:mvn:user:other";

        // Act & Assert
        Assert.False(repo.HasEditPermission(series.id, otherUserUrn));
    }

    [Fact]
    public void GrantEditPermission_AddsUserToAllowedEditors_ForUnit()
    {
        // Arrange
        var repo = CreateTestRepository();
        var series = CreateTestSeries();
        repo.AddSeries(series);
        var unit = CreateTestUnit(series.id, 1);
        repo.AddUnit(unit);
        var userUrn = "urn:mvn:user:editor1";
        var grantedBy = "urn:mvn:user:owner";

        // Act
        repo.GrantEditPermission(unit.id, userUrn, grantedBy);

        // Assert
        Assert.True(repo.HasEditPermission(unit.id, userUrn));
        var permissions = repo.GetEditPermissions(unit.id);
        Assert.Contains(userUrn, permissions);
        
        // Verify unit object is updated
        var updatedUnit = repo.GetUnit(unit.id);
        Assert.NotNull(updatedUnit?.allowed_editors);
        Assert.Contains(userUrn, updatedUnit.allowed_editors);
    }

    [Fact]
    public void HasEditPermission_ReturnsTrueForUnitOwner()
    {
        // Arrange
        var repo = CreateTestRepository();
        var series = CreateTestSeries();
        repo.AddSeries(series);
        var uploaderUrn = "urn:mvn:user:uploader1";
        var unit = CreateTestUnit(series.id, 1, uploaderUrn);
        repo.AddUnit(unit);

        // Act & Assert
        Assert.True(repo.HasEditPermission(unit.id, uploaderUrn));
    }

    [Fact]
    public void HasEditPermission_ReturnsTrueForSeriesOwner_OnUnit()
    {
        // Arrange
        var repo = CreateTestRepository();
        var ownerUrn = "urn:mvn:user:owner";
        var series = CreateTestSeries(ownerUrn);
        repo.AddSeries(series);
        var uploaderUrn = "urn:mvn:user:uploader1";
        var unit = CreateTestUnit(series.id, 1, uploaderUrn);
        repo.AddUnit(unit);

        // Act & Assert - Series owner should have permission on units
        Assert.True(repo.HasEditPermission(unit.id, ownerUrn));
    }

    [Fact]
    public void GetEditPermissions_ReturnsAllGrantedUsers()
    {
        // Arrange
        var repo = CreateTestRepository();
        var series = CreateTestSeries();
        repo.AddSeries(series);
        var user1 = "urn:mvn:user:editor1";
        var user2 = "urn:mvn:user:editor2";
        var user3 = "urn:mvn:user:editor3";
        var grantedBy = "urn:mvn:user:owner";

        // Act
        repo.GrantEditPermission(series.id, user1, grantedBy);
        repo.GrantEditPermission(series.id, user2, grantedBy);
        repo.GrantEditPermission(series.id, user3, grantedBy);

        // Assert
        var permissions = repo.GetEditPermissions(series.id);
        Assert.Equal(3, permissions.Length);
        Assert.Contains(user1, permissions);
        Assert.Contains(user2, permissions);
        Assert.Contains(user3, permissions);
    }

    [Fact]
    public void GrantEditPermission_UpdatesExistingPermission_WhenGrantedAgain()
    {
        // Arrange
        var repo = CreateTestRepository();
        var series = CreateTestSeries();
        repo.AddSeries(series);
        var userUrn = "urn:mvn:user:editor1";
        var grantedBy = "urn:mvn:user:owner";

        // Act - Grant twice
        repo.GrantEditPermission(series.id, userUrn, grantedBy);
        repo.GrantEditPermission(series.id, userUrn, grantedBy);

        // Assert - Should still only have one permission entry
        var permissions = repo.GetEditPermissions(series.id);
        Assert.Single(permissions);
        Assert.Contains(userUrn, permissions);
    }

    // Helper methods
    private static Series CreateTestSeries(string? ownerId = null)
    {
        return new Series(
            id: UrnHelper.CreateSeriesUrn(),
            federation_ref: "urn:mvn:node:local",
            title: "Test Series",
            description: "Test",
            poster: new Poster("url", "alt"),
            media_type: MediaTypes.Photo,
            external_links: new Dictionary<string, string>(),
            reading_direction: ReadingDirections.RTL,
            tags: new[] { "Action" },
            content_warnings: [],
            authors: [],
            scanlators: [],
            groups: null,
            alt_titles: null,
            status: "Ongoing",
            year: 2024,
            created_by: ownerId ?? "urn:mvn:user:owner",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );
    }

    private static Unit CreateTestUnit(string seriesId, int number, string? createdBy = null)
    {
        return new Unit(
            id: UrnHelper.CreateUnitUrn(),
            series_id: seriesId,
            unit_number: number,
            title: $"Chapter {number}",
            created_at: DateTime.UtcNow,
            created_by: createdBy ?? "urn:mvn:user:uploader1",
            language: "en",
            page_count: 0,
            folder_path: null,
            updated_at: DateTime.UtcNow,
            description: null,
            tags: null,
            content_warnings: null,
            authors: null,
            localized: null
        );
    }
}
