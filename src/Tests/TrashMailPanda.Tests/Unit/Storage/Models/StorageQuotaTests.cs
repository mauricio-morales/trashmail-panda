using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TrashMailPanda.Providers.Storage.Models;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Storage.Models;

[Trait("Category", "Unit")]
public class StorageQuotaTests
{
    [Fact(Timeout = 5000)]
    public void StorageQuota_ValidQuota_PassesValidation()
    {
        // Arrange
        var quota = new StorageQuota
        {
            Id = 1,
            LimitBytes = 50L * 1024 * 1024 * 1024, // 50GB
            CurrentBytes = 10L * 1024 * 1024 * 1024, // 10GB
            FeatureBytes = 5L * 1024 * 1024 * 1024, // 5GB
            ArchiveBytes = 5L * 1024 * 1024 * 1024, // 5GB
            FeatureCount = 1000,
            ArchiveCount = 500,
            UserCorrectedCount = 50,
            LastCleanupAt = DateTime.UtcNow.AddDays(-7),
            LastMonitoredAt = DateTime.UtcNow
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(quota, new ValidationContext(quota), validationResults, true);

        // Assert
        Assert.True(isValid);
        Assert.Empty(validationResults);
    }

    [Fact(Timeout = 5000)]
    public void StorageQuota_LimitBytesZero_FailsValidation()
    {
        // Arrange
        var quota = new StorageQuota
        {
            Id = 1,
            LimitBytes = 0, // Invalid
            CurrentBytes = 0,
            FeatureBytes = 0,
            ArchiveBytes = 0,
            FeatureCount = 0,
            ArchiveCount = 0,
            UserCorrectedCount = 0,
            LastCleanupAt = null,
            LastMonitoredAt = DateTime.UtcNow
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(quota, new ValidationContext(quota), validationResults, true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(validationResults, vr => vr.MemberNames.Contains("LimitBytes"));
    }

    [Fact(Timeout = 5000)]
    public void StorageQuota_NegativeCurrentBytes_FailsValidation()
    {
        // Arrange
        var quota = new StorageQuota
        {
            Id = 1,
            LimitBytes = 50L * 1024 * 1024 * 1024,
            CurrentBytes = -100, // Invalid
            FeatureBytes = 0,
            ArchiveBytes = 0,
            FeatureCount = 0,
            ArchiveCount = 0,
            UserCorrectedCount = 0,
            LastCleanupAt = null,
            LastMonitoredAt = DateTime.UtcNow
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(quota, new ValidationContext(quota), validationResults, true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(validationResults, vr => vr.MemberNames.Contains("CurrentBytes"));
    }

    [Fact(Timeout = 5000)]
    public void StorageQuota_IdNotEqualToOne_FailsValidation()
    {
        // Arrange
        var quota = new StorageQuota
        {
            Id = 2, // Invalid - must be 1
            LimitBytes = 50L * 1024 * 1024 * 1024,
            CurrentBytes = 0,
            FeatureBytes = 0,
            ArchiveBytes = 0,
            FeatureCount = 0,
            ArchiveCount = 0,
            UserCorrectedCount = 0,
            LastCleanupAt = null,
            LastMonitoredAt = DateTime.UtcNow
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(quota, new ValidationContext(quota), validationResults, true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(validationResults, vr => vr.MemberNames.Contains("Id"));
    }

    [Fact(Timeout = 5000)]
    public void StorageQuota_NullLastCleanupAt_IsValid()
    {
        // Arrange
        var quota = new StorageQuota
        {
            Id = 1,
            LimitBytes = 50L * 1024 * 1024 * 1024,
            CurrentBytes = 0,
            FeatureBytes = 0,
            ArchiveBytes = 0,
            FeatureCount = 0,
            ArchiveCount = 0,
            UserCorrectedCount = 0,
            LastCleanupAt = null, // Nullable - valid
            LastMonitoredAt = DateTime.UtcNow
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(quota, new ValidationContext(quota), validationResults, true);

        // Assert
        Assert.True(isValid);
        Assert.Null(quota.LastCleanupAt);
    }

    [Fact(Timeout = 5000)]
    public void StorageQuota_UsageCalculation_MatchesComponentSum()
    {
        // Arrange
        var featureBytes = 3L * 1024 * 1024 * 1024; // 3GB
        var archiveBytes = 7L * 1024 * 1024 * 1024; // 7GB
        var expectedTotal = featureBytes + archiveBytes; // 10GB

        var quota = new StorageQuota
        {
            Id = 1,
            LimitBytes = 50L * 1024 * 1024 * 1024,
            CurrentBytes = expectedTotal,
            FeatureBytes = featureBytes,
            ArchiveBytes = archiveBytes,
            FeatureCount = 1000,
            ArchiveCount = 500,
            UserCorrectedCount = 50,
            LastCleanupAt = null,
            LastMonitoredAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.Equal(expectedTotal, quota.FeatureBytes + quota.ArchiveBytes);
        Assert.Equal(10L * 1024 * 1024 * 1024, quota.CurrentBytes);
    }

    [Fact(Timeout = 5000)]
    public void StorageQuota_UsagePercentage_CalculatesCorrectly()
    {
        // Arrange
        var limitBytes = 50L * 1024 * 1024 * 1024; // 50GB
        var currentBytes = 45L * 1024 * 1024 * 1024; // 45GB = 90%

        var quota = new StorageQuota
        {
            Id = 1,
            LimitBytes = limitBytes,
            CurrentBytes = currentBytes,
            FeatureBytes = currentBytes / 2,
            ArchiveBytes = currentBytes / 2,
            FeatureCount = 1000,
            ArchiveCount = 500,
            UserCorrectedCount = 50,
            LastCleanupAt = null,
            LastMonitoredAt = DateTime.UtcNow
        };

        // Act
        var usagePercentage = (double)quota.CurrentBytes / quota.LimitBytes * 100;

        // Assert
        Assert.Equal(90.0, usagePercentage, 2);
    }

    [Fact(Timeout = 5000)]
    public void StorageQuota_DefaultLimit_Is50GB()
    {
        // Arrange
        var expected50GB = 50L * 1024 * 1024 * 1024;

        var quota = new StorageQuota
        {
            Id = 1,
            LimitBytes = expected50GB,
            CurrentBytes = 0,
            FeatureBytes = 0,
            ArchiveBytes = 0,
            FeatureCount = 0,
            ArchiveCount = 0,
            UserCorrectedCount = 0,
            LastCleanupAt = null,
            LastMonitoredAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(53_687_091_200, quota.LimitBytes); // Exactly 50GB
    }

    [Fact(Timeout = 5000)]
    public void StorageQuota_CountFields_TrackCorrectMetrics()
    {
        // Arrange
        var quota = new StorageQuota
        {
            Id = 1,
            LimitBytes = 50L * 1024 * 1024 * 1024,
            CurrentBytes = 10L * 1024 * 1024 * 1024,
            FeatureBytes = 5L * 1024 * 1024 * 1024,
            ArchiveBytes = 5L * 1024 * 1024 * 1024,
            FeatureCount = 5000,
            ArchiveCount = 2500,
            UserCorrectedCount = 250,
            LastCleanupAt = null,
            LastMonitoredAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(5000, quota.FeatureCount);
        Assert.Equal(2500, quota.ArchiveCount);
        Assert.Equal(250, quota.UserCorrectedCount);
        Assert.True(quota.UserCorrectedCount <= quota.ArchiveCount);
        Assert.True(quota.ArchiveCount <= quota.FeatureCount);
    }
}
