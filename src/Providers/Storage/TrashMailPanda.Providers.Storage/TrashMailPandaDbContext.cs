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

    /// <summary>
    /// Gmail training email records imported from training scans.
    /// </summary>
    public DbSet<TrainingEmailEntity> TrainingEmails => Set<TrainingEmailEntity>();

    /// <summary>
    /// User's complete Gmail label catalog (user-created and system labels).
    /// </summary>
    public DbSet<LabelTaxonomyEntity> LabelTaxonomy => Set<LabelTaxonomyEntity>();

    /// <summary>
    /// Links training emails to Gmail labels.
    /// </summary>
    public DbSet<LabelAssociationEntity> LabelAssociations => Set<LabelAssociationEntity>();

    /// <summary>
    /// Training data scan progress: per-folder cursors, resumability state, and incremental historyId.
    /// </summary>
    public DbSet<ScanProgressEntity> ScanProgress => Set<ScanProgressEntity>();

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

            // Attachment metadata columns — DEFAULT 0 so existing rows are valid after migration
            entity.Property(e => e.AttachmentCount).HasDefaultValue(0);
            entity.Property(e => e.TotalAttachmentSizeLog).HasDefaultValue(0f);
            entity.Property(e => e.HasDocAttachments).HasDefaultValue(0);
            entity.Property(e => e.HasImageAttachments).HasDefaultValue(0);
            entity.Property(e => e.HasAudioAttachments).HasDefaultValue(0);
            entity.Property(e => e.HasVideoAttachments).HasDefaultValue(0);
            entity.Property(e => e.HasXmlAttachments).HasDefaultValue(0);
            entity.Property(e => e.HasBinaryAttachments).HasDefaultValue(0);
            entity.Property(e => e.HasOtherAttachments).HasDefaultValue(0);

            // Create indexes for common queries
            entity.HasIndex(e => e.FeatureSchemaVersion).HasDatabaseName("idx_email_features_schema_version");
            entity.HasIndex(e => e.ExtractedAt).HasDatabaseName("idx_email_features_extracted_at");
            entity.HasIndex(e => e.UserCorrected).HasDatabaseName("idx_email_features_user_corrected");
            entity.HasIndex(e => e.ReceivedDateUtc).HasDatabaseName("idx_email_features_received_date_utc");
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

        // TrainingEmailEntity configuration
        modelBuilder.Entity<TrainingEmailEntity>(entity =>
        {
            entity.ToTable("training_emails");
            entity.HasKey(e => e.EmailId);
            entity.Property(e => e.EmailId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.AccountId).HasMaxLength(320).IsRequired();
            entity.Property(e => e.ThreadId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.FolderOrigin).HasMaxLength(20).IsRequired();
            entity.Property(e => e.SubjectPrefix).HasMaxLength(10);
            entity.Property(e => e.ClassificationSignal).HasMaxLength(20).IsRequired();
            entity.Property(e => e.RawLabelIds).HasMaxLength(2000);
            entity.Property(e => e.LastSeenAt).IsRequired();
            entity.Property(e => e.ImportedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasIndex(e => e.AccountId).HasDatabaseName("idx_training_emails_account");
            entity.HasIndex(e => new { e.ClassificationSignal, e.IsValid }).HasDatabaseName("idx_training_emails_signal");
            entity.HasIndex(e => e.LastSeenAt).HasDatabaseName("idx_training_emails_last_seen");
            entity.HasIndex(e => e.ThreadId).HasDatabaseName("idx_training_emails_thread");

            entity.HasMany(e => e.LabelAssociations)
                  .WithOne(a => a.TrainingEmail)
                  .HasForeignKey(a => a.EmailId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // LabelTaxonomyEntity configuration
        modelBuilder.Entity<LabelTaxonomyEntity>(entity =>
        {
            entity.ToTable("label_taxonomy");
            entity.HasKey(e => e.LabelId);
            entity.Property(e => e.LabelId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.AccountId).HasMaxLength(320).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Color).HasMaxLength(50);
            entity.Property(e => e.LabelType).HasMaxLength(10).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasIndex(e => new { e.AccountId, e.LabelType }).HasDatabaseName("idx_label_taxonomy_account");
            entity.HasIndex(e => e.UsageCount).HasDatabaseName("idx_label_taxonomy_usage");

            entity.HasMany(e => e.LabelAssociations)
                  .WithOne(a => a.LabelTaxonomy)
                  .HasForeignKey(a => a.LabelId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // LabelAssociationEntity configuration
        modelBuilder.Entity<LabelAssociationEntity>(entity =>
        {
            entity.ToTable("label_associations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EmailId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.LabelId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            // Unique constraint: one association per email-label pair
            entity.HasIndex(e => new { e.EmailId, e.LabelId })
                  .IsUnique()
                  .HasDatabaseName("idx_label_assoc_unique");

            entity.HasIndex(e => e.EmailId).HasDatabaseName("idx_label_assoc_email");
            entity.HasIndex(e => new { e.LabelId, e.IsTrainingSignal }).HasDatabaseName("idx_label_assoc_label");
        });

        // ScanProgressEntity configuration
        modelBuilder.Entity<ScanProgressEntity>(entity =>
        {
            entity.ToTable("scan_progress");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AccountId).HasMaxLength(320).IsRequired();
            entity.Property(e => e.ScanType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.FolderProgressJson).HasMaxLength(4000);
            entity.Property(e => e.LastProcessedEmailId).HasMaxLength(500);
            entity.Property(e => e.StartedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasIndex(e => new { e.AccountId, e.Status }).HasDatabaseName("idx_scan_progress_account_status");
        });
    }
}
