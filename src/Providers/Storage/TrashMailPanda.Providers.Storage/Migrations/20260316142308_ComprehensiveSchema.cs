using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <inheritdoc />
    public partial class ComprehensiveSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_config",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_config", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "classification_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    email_id = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    classification = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    confidence = table.Column<double>(type: "REAL", nullable: false),
                    reasons = table.Column<string>(type: "TEXT", nullable: false),
                    user_action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    user_feedback = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    batch_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classification_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_archive",
                columns: table => new
                {
                    EmailId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ThreadId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ProviderType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    HeadersJson = table.Column<string>(type: "TEXT", nullable: false),
                    BodyText = table.Column<string>(type: "TEXT", nullable: true),
                    BodyHtml = table.Column<string>(type: "TEXT", nullable: true),
                    FolderTagsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SourceFolder = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SizeEstimate = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Snippet = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UserCorrected = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_archive", x => x.EmailId);
                });

            migrationBuilder.CreateTable(
                name: "email_features",
                columns: table => new
                {
                    EmailId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SenderDomain = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    SenderKnown = table.Column<int>(type: "INTEGER", nullable: false),
                    ContactStrength = table.Column<int>(type: "INTEGER", nullable: false),
                    SpfResult = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DkimResult = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DmarcResult = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    HasListUnsubscribe = table.Column<int>(type: "INTEGER", nullable: false),
                    HasAttachments = table.Column<int>(type: "INTEGER", nullable: false),
                    HourReceived = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    EmailSizeLog = table.Column<float>(type: "REAL", nullable: false),
                    SubjectLength = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipientCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsReply = table.Column<int>(type: "INTEGER", nullable: false),
                    InUserWhitelist = table.Column<int>(type: "INTEGER", nullable: false),
                    InUserBlacklist = table.Column<int>(type: "INTEGER", nullable: false),
                    LabelCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HasTrackingPixel = table.Column<int>(type: "INTEGER", nullable: false),
                    UnsubscribeLinkInBody = table.Column<int>(type: "INTEGER", nullable: false),
                    EmailAgeDays = table.Column<int>(type: "INTEGER", nullable: false),
                    IsInInbox = table.Column<int>(type: "INTEGER", nullable: false),
                    IsStarred = table.Column<int>(type: "INTEGER", nullable: false),
                    IsImportant = table.Column<int>(type: "INTEGER", nullable: false),
                    WasInTrash = table.Column<int>(type: "INTEGER", nullable: false),
                    WasInSpam = table.Column<int>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<int>(type: "INTEGER", nullable: false),
                    ThreadMessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SenderFrequency = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    BodyTextShort = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TopicClusterId = table.Column<int>(type: "INTEGER", nullable: true),
                    TopicDistributionJson = table.Column<string>(type: "TEXT", nullable: true),
                    SenderCategory = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SemanticEmbeddingJson = table.Column<string>(type: "TEXT", nullable: true),
                    FeatureSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ExtractedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserCorrected = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_features", x => x.EmailId);
                });

            migrationBuilder.CreateTable(
                name: "email_metadata",
                columns: table => new
                {
                    email_id = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    folder_id = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    folder_name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    subject = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    sender_email = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    sender_name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    received_date = table.Column<DateTime>(type: "TEXT", nullable: true),
                    classification = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    confidence = table.Column<double>(type: "REAL", nullable: true),
                    reasons = table.Column<string>(type: "TEXT", nullable: true),
                    bulk_key = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    last_classified = table.Column<DateTime>(type: "TEXT", nullable: true),
                    user_action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    user_action_timestamp = table.Column<DateTime>(type: "TEXT", nullable: true),
                    processing_batch_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_metadata", x => x.email_id);
                });

            migrationBuilder.CreateTable(
                name: "encrypted_credentials",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    encrypted_value = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encrypted_credentials", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "encrypted_tokens",
                columns: table => new
                {
                    provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    encrypted_token = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encrypted_tokens", x => x.provider);
                });

            migrationBuilder.CreateTable(
                name: "feature_schema_versions",
                columns: table => new
                {
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FeatureCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_schema_versions", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "schema_version",
                columns: table => new
                {
                    version = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    applied_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schema_version", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "storage_quota",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LimitBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrentBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FeatureBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ArchiveBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FeatureCount = table.Column<long>(type: "INTEGER", nullable: false),
                    ArchiveCount = table.Column<long>(type: "INTEGER", nullable: false),
                    UserCorrectedCount = table.Column<long>(type: "INTEGER", nullable: false),
                    LastCleanupAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastMonitoredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_quota", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    rule_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    rule_key = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    rule_value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_classification_email",
                table: "classification_history",
                column: "email_id");

            migrationBuilder.CreateIndex(
                name: "idx_classification_timestamp",
                table: "classification_history",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "idx_email_archive_archived_at",
                table: "email_archive",
                column: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "idx_email_archive_user_corrected",
                table: "email_archive",
                column: "UserCorrected");

            migrationBuilder.CreateIndex(
                name: "idx_email_features_extracted_at",
                table: "email_features",
                column: "ExtractedAt");

            migrationBuilder.CreateIndex(
                name: "idx_email_features_schema_version",
                table: "email_features",
                column: "FeatureSchemaVersion");

            migrationBuilder.CreateIndex(
                name: "idx_email_features_user_corrected",
                table: "email_features",
                column: "UserCorrected");

            migrationBuilder.CreateIndex(
                name: "idx_email_classification",
                table: "email_metadata",
                column: "classification");

            migrationBuilder.CreateIndex(
                name: "idx_email_user_action",
                table: "email_metadata",
                column: "user_action");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_config");

            migrationBuilder.DropTable(
                name: "classification_history");

            migrationBuilder.DropTable(
                name: "email_archive");

            migrationBuilder.DropTable(
                name: "email_features");

            migrationBuilder.DropTable(
                name: "email_metadata");

            migrationBuilder.DropTable(
                name: "encrypted_credentials");

            migrationBuilder.DropTable(
                name: "encrypted_tokens");

            migrationBuilder.DropTable(
                name: "feature_schema_versions");

            migrationBuilder.DropTable(
                name: "schema_version");

            migrationBuilder.DropTable(
                name: "storage_quota");

            migrationBuilder.DropTable(
                name: "user_rules");
        }
    }
}
