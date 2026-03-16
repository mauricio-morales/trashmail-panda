using Microsoft.EntityFrameworkCore;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// EF Core DbContext for TrashMail Panda encrypted SQLite database.
/// Manages all application data including email features, archives, and user rules.
/// </summary>
public class TrashMailPandaDbContext : DbContext
{
    public TrashMailPandaDbContext(DbContextOptions<TrashMailPandaDbContext> options)
        : base(options)
    {
    }

    // ============================================================
    // DBSETS
    // ============================================================

    /// <summary>
    /// Email feature vectors for ML training (never deleted during cleanup).
    /// </summary>
    public DbSet<EmailFeatureVector> EmailFeatures => Set<EmailFeatureVector>();

    /// <summary>
    /// Complete email archives for regeneration and audit trails.
    /// </summary>
    public DbSet<EmailArchiveEntry> EmailArchives => Set<EmailArchiveEntry>();

    /// <summary>
    /// Storage quota monitoring and usage tracking.
    /// </summary>
    public DbSet<StorageQuota> StorageQuotas => Set<StorageQuota>();

    /// <summary>
    /// Feature schema version tracking.
    /// </summary>
    public DbSet<FeatureSchema> FeatureSchemas => Set<FeatureSchema>();

    // ============================================================
    // DBSETS - Core Application Tables
    // ============================================================

    /// <summary>
    /// User-defined email filtering rules.
    /// </summary>
    public DbSet<UserRuleEntity> UserRules => Set<UserRuleEntity>();

    /// <summary>
    /// Email classification metadata and processing state.
    /// </summary>
    public DbSet<EmailMetadataEntity> EmailMetadata => Set<EmailMetadataEntity>();

    /// <summary>
    /// Historical record of email classification decisions.
    /// </summary>
    public DbSet<ClassificationHistoryEntity> ClassificationHistory => Set<ClassificationHistoryEntity>();

    /// <summary>
    /// Application configuration key-value storage.
    /// </summary>
    public DbSet<AppConfigEntity> AppConfig => Set<AppConfigEntity>();

    /// <summary>
    /// Encrypted OAuth tokens storage.
    /// </summary>
    public DbSet<EncryptedTokenEntity> EncryptedTokens => Set<EncryptedTokenEntity>();

    /// <summary>
    /// Generic encrypted credentials storage.
    /// </summary>
    public DbSet<EncryptedCredentialEntity> EncryptedCredentials => Set<EncryptedCredentialEntity>();

    /// <summary>
    /// Database migration version tracking.
    /// </summary>
    public DbSet<SchemaVersionEntity> SchemaVersions => Set<SchemaVersionEntity>();

    // ============================================================
    // MODEL CONFIGURATION
    // ============================================================

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // EmailFeatureVector configuration
        modelBuilder.Entity<EmailFeatureVector>(entity =>
        {
            entity.ToTable("email_features");
            entity.HasKey(e => e.EmailId);
            entity.Property(e => e.EmailId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.SenderDomain).HasMaxLength(255).IsRequired();
            entity.Property(e => e.SpfResult).HasMaxLength(20).IsRequired();
            entity.Property(e => e.DkimResult).HasMaxLength(20).IsRequired();
            entity.Property(e => e.DmarcResult).HasMaxLength(20).IsRequired();
            entity.Property(e => e.SubjectText).HasMaxLength(1000);
            entity.Property(e => e.BodyTextShort).HasMaxLength(500);
            entity.Property(e => e.SenderCategory).HasMaxLength(100);
            entity.Property(e => e.FeatureSchemaVersion).IsRequired();
            entity.Property(e => e.ExtractedAt).IsRequired();
            entity.Property(e => e.UserCorrected).IsRequired();

            // Create indexes for common queries
            entity.HasIndex(e => e.FeatureSchemaVersion).HasDatabaseName("idx_email_features_schema_version");
            entity.HasIndex(e => e.ExtractedAt).HasDatabaseName("idx_email_features_extracted_at");
            entity.HasIndex(e => e.UserCorrected).HasDatabaseName("idx_email_features_user_corrected");
        });

        // EmailArchiveEntry configuration
        modelBuilder.Entity<EmailArchiveEntry>(entity =>
        {
            entity.ToTable("email_archive");
            entity.HasKey(e => e.EmailId);
            entity.Property(e => e.EmailId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ThreadId).HasMaxLength(500);
            entity.Property(e => e.ProviderType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.HeadersJson).IsRequired();
            entity.Property(e => e.FolderTagsJson).IsRequired();
            entity.Property(e => e.SourceFolder).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ReceivedDate).IsRequired();
            entity.Property(e => e.ArchivedAt).IsRequired();
            entity.Property(e => e.Snippet).HasMaxLength(200);
            entity.Property(e => e.UserCorrected).IsRequired();

            // Create indexes for common queries and cleanup
            entity.HasIndex(e => e.ArchivedAt).HasDatabaseName("idx_email_archive_archived_at");
            entity.HasIndex(e => e.UserCorrected).HasDatabaseName("idx_email_archive_user_corrected");
        });

        // StorageQuota configuration
        modelBuilder.Entity<StorageQuota>(entity =>
        {
            entity.ToTable("storage_quota");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LimitBytes).IsRequired();
            entity.Property(e => e.CurrentBytes).IsRequired();
            entity.Property(e => e.LastMonitoredAt).IsRequired();
        });

        // FeatureSchema configuration
        modelBuilder.Entity<FeatureSchema>(entity =>
        {
            entity.ToTable("feature_schema_versions");
            entity.HasKey(e => e.Version);
            entity.Property(e => e.Version).IsRequired();
            entity.Property(e => e.FeatureCount).IsRequired();
            entity.Property(e => e.AppliedAt).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500).IsRequired();
        });

        // UserRuleEntity configuration
        modelBuilder.Entity<UserRuleEntity>(entity =>
        {
            entity.ToTable("user_rules");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RuleType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.RuleKey).HasMaxLength(50).IsRequired();
            entity.Property(e => e.RuleValue).HasMaxLength(500).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
        });

        // EmailMetadataEntity configuration
        modelBuilder.Entity<EmailMetadataEntity>(entity =>
        {
            entity.ToTable("email_metadata");
            entity.HasKey(e => e.EmailId);
            entity.Property(e => e.EmailId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.FolderId).HasMaxLength(500);
            entity.Property(e => e.FolderName).HasMaxLength(255);
            entity.Property(e => e.Subject).HasMaxLength(2000);
            entity.Property(e => e.SenderEmail).HasMaxLength(500);
            entity.Property(e => e.SenderName).HasMaxLength(500);
            entity.Property(e => e.Classification).HasMaxLength(50);
            entity.Property(e => e.UserAction).HasMaxLength(50);
            entity.Property(e => e.ProcessingBatchId).HasMaxLength(100);

            entity.HasIndex(e => e.Classification).HasDatabaseName("idx_email_classification");
            entity.HasIndex(e => e.UserAction).HasDatabaseName("idx_email_user_action");
        });

        // ClassificationHistoryEntity configuration
        modelBuilder.Entity<ClassificationHistoryEntity>(entity =>
        {
            entity.ToTable("classification_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.EmailId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Classification).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Confidence).IsRequired();
            entity.Property(e => e.ReasonsJson).IsRequired();
            entity.Property(e => e.UserAction).HasMaxLength(50);
            entity.Property(e => e.UserFeedback).HasMaxLength(50);
            entity.Property(e => e.BatchId).HasMaxLength(100);

            entity.HasIndex(e => e.Timestamp).HasDatabaseName("idx_classification_timestamp");
            entity.HasIndex(e => e.EmailId).HasDatabaseName("idx_classification_email");
        });

        // AppConfigEntity configuration
        modelBuilder.Entity<AppConfigEntity>(entity =>
        {
            entity.ToTable("app_config");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Value).IsRequired();
        });

        // EncryptedTokenEntity configuration
        modelBuilder.Entity<EncryptedTokenEntity>(entity =>
        {
            entity.ToTable("encrypted_tokens");
            entity.HasKey(e => e.Provider);
            entity.Property(e => e.Provider).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EncryptedToken).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        // EncryptedCredentialEntity configuration
        modelBuilder.Entity<EncryptedCredentialEntity>(entity =>
        {
            entity.ToTable("encrypted_credentials");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(255).IsRequired();
            entity.Property(e => e.EncryptedValue).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        // SchemaVersionEntity configuration
        modelBuilder.Entity<SchemaVersionEntity>(entity =>
        {
            entity.ToTable("schema_version");
            entity.HasKey(e => e.Version);
            entity.Property(e => e.Version).IsRequired();
            entity.Property(e => e.AppliedAt).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500).IsRequired();
        });
    }
}
