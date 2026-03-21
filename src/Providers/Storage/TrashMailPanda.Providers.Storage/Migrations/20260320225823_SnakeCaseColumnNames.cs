using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <inheritdoc />
    public partial class SnakeCaseColumnNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_label_associations_label_taxonomy_LabelId",
                table: "label_associations");

            migrationBuilder.DropForeignKey(
                name: "FK_label_associations_training_emails_EmailId",
                table: "label_associations");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "training_emails",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "ThreadId",
                table: "training_emails",
                newName: "thread_id");

            migrationBuilder.RenameColumn(
                name: "SubjectPrefix",
                table: "training_emails",
                newName: "subject_prefix");

            migrationBuilder.RenameColumn(
                name: "SignalConfidence",
                table: "training_emails",
                newName: "signal_confidence");

            migrationBuilder.RenameColumn(
                name: "RawLabelIds",
                table: "training_emails",
                newName: "raw_label_ids");

            migrationBuilder.RenameColumn(
                name: "LastSeenAt",
                table: "training_emails",
                newName: "last_seen_at");

            migrationBuilder.RenameColumn(
                name: "IsValid",
                table: "training_emails",
                newName: "is_valid");

            migrationBuilder.RenameColumn(
                name: "IsReplied",
                table: "training_emails",
                newName: "is_replied");

            migrationBuilder.RenameColumn(
                name: "IsRead",
                table: "training_emails",
                newName: "is_read");

            migrationBuilder.RenameColumn(
                name: "IsForwarded",
                table: "training_emails",
                newName: "is_forwarded");

            migrationBuilder.RenameColumn(
                name: "ImportedAt",
                table: "training_emails",
                newName: "imported_at");

            migrationBuilder.RenameColumn(
                name: "FolderOrigin",
                table: "training_emails",
                newName: "folder_origin");

            migrationBuilder.RenameColumn(
                name: "ClassificationSignal",
                table: "training_emails",
                newName: "classification_signal");

            migrationBuilder.RenameColumn(
                name: "AccountId",
                table: "training_emails",
                newName: "account_id");

            migrationBuilder.RenameColumn(
                name: "EmailId",
                table: "training_emails",
                newName: "email_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "storage_quota",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserCorrectedCount",
                table: "storage_quota",
                newName: "user_corrected_count");

            migrationBuilder.RenameColumn(
                name: "LimitBytes",
                table: "storage_quota",
                newName: "limit_bytes");

            migrationBuilder.RenameColumn(
                name: "LastMonitoredAt",
                table: "storage_quota",
                newName: "last_monitored_at");

            migrationBuilder.RenameColumn(
                name: "LastCleanupAt",
                table: "storage_quota",
                newName: "last_cleanup_at");

            migrationBuilder.RenameColumn(
                name: "FeatureCount",
                table: "storage_quota",
                newName: "feature_count");

            migrationBuilder.RenameColumn(
                name: "FeatureBytes",
                table: "storage_quota",
                newName: "feature_bytes");

            migrationBuilder.RenameColumn(
                name: "CurrentBytes",
                table: "storage_quota",
                newName: "current_bytes");

            migrationBuilder.RenameColumn(
                name: "ArchiveCount",
                table: "storage_quota",
                newName: "archive_count");

            migrationBuilder.RenameColumn(
                name: "ArchiveBytes",
                table: "storage_quota",
                newName: "archive_bytes");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "scan_progress",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "scan_progress",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "scan_progress",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TotalEstimate",
                table: "scan_progress",
                newName: "total_estimate");

            migrationBuilder.RenameColumn(
                name: "StartedAt",
                table: "scan_progress",
                newName: "started_at");

            migrationBuilder.RenameColumn(
                name: "ScanType",
                table: "scan_progress",
                newName: "scan_type");

            migrationBuilder.RenameColumn(
                name: "ProcessedCount",
                table: "scan_progress",
                newName: "processed_count");

            migrationBuilder.RenameColumn(
                name: "LastProcessedEmailId",
                table: "scan_progress",
                newName: "last_processed_email_id");

            migrationBuilder.RenameColumn(
                name: "HistoryId",
                table: "scan_progress",
                newName: "history_id");

            migrationBuilder.RenameColumn(
                name: "FolderProgressJson",
                table: "scan_progress",
                newName: "folder_progress_json");

            migrationBuilder.RenameColumn(
                name: "CompletedAt",
                table: "scan_progress",
                newName: "completed_at");

            migrationBuilder.RenameColumn(
                name: "AccountId",
                table: "scan_progress",
                newName: "account_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "label_taxonomy",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Color",
                table: "label_taxonomy",
                newName: "color");

            migrationBuilder.RenameColumn(
                name: "UsageCount",
                table: "label_taxonomy",
                newName: "usage_count");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "label_taxonomy",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "LabelType",
                table: "label_taxonomy",
                newName: "label_type");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "label_taxonomy",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "AccountId",
                table: "label_taxonomy",
                newName: "account_id");

            migrationBuilder.RenameColumn(
                name: "LabelId",
                table: "label_taxonomy",
                newName: "label_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "label_associations",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "LabelId",
                table: "label_associations",
                newName: "label_id");

            migrationBuilder.RenameColumn(
                name: "IsTrainingSignal",
                table: "label_associations",
                newName: "is_training_signal");

            migrationBuilder.RenameColumn(
                name: "IsContextFeature",
                table: "label_associations",
                newName: "is_context_feature");

            migrationBuilder.RenameColumn(
                name: "EmailId",
                table: "label_associations",
                newName: "email_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "label_associations",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "WasInTrash",
                table: "email_features",
                newName: "was_in_trash");

            migrationBuilder.RenameColumn(
                name: "WasInSpam",
                table: "email_features",
                newName: "was_in_spam");

            migrationBuilder.RenameColumn(
                name: "UserCorrected",
                table: "email_features",
                newName: "user_corrected");

            migrationBuilder.RenameColumn(
                name: "UnsubscribeLinkInBody",
                table: "email_features",
                newName: "unsubscribe_link_in_body");

            migrationBuilder.RenameColumn(
                name: "TrainingLabel",
                table: "email_features",
                newName: "training_label");

            migrationBuilder.RenameColumn(
                name: "TopicDistributionJson",
                table: "email_features",
                newName: "topic_distribution_json");

            migrationBuilder.RenameColumn(
                name: "TopicClusterId",
                table: "email_features",
                newName: "topic_cluster_id");

            migrationBuilder.RenameColumn(
                name: "ThreadMessageCount",
                table: "email_features",
                newName: "thread_message_count");

            migrationBuilder.RenameColumn(
                name: "SubjectText",
                table: "email_features",
                newName: "subject_text");

            migrationBuilder.RenameColumn(
                name: "SubjectLength",
                table: "email_features",
                newName: "subject_length");

            migrationBuilder.RenameColumn(
                name: "SpfResult",
                table: "email_features",
                newName: "spf_result");

            migrationBuilder.RenameColumn(
                name: "SenderKnown",
                table: "email_features",
                newName: "sender_known");

            migrationBuilder.RenameColumn(
                name: "SenderFrequency",
                table: "email_features",
                newName: "sender_frequency");

            migrationBuilder.RenameColumn(
                name: "SenderDomain",
                table: "email_features",
                newName: "sender_domain");

            migrationBuilder.RenameColumn(
                name: "SenderCategory",
                table: "email_features",
                newName: "sender_category");

            migrationBuilder.RenameColumn(
                name: "SemanticEmbeddingJson",
                table: "email_features",
                newName: "semantic_embedding_json");

            migrationBuilder.RenameColumn(
                name: "RecipientCount",
                table: "email_features",
                newName: "recipient_count");

            migrationBuilder.RenameColumn(
                name: "LinkCount",
                table: "email_features",
                newName: "link_count");

            migrationBuilder.RenameColumn(
                name: "LabelCount",
                table: "email_features",
                newName: "label_count");

            migrationBuilder.RenameColumn(
                name: "IsStarred",
                table: "email_features",
                newName: "is_starred");

            migrationBuilder.RenameColumn(
                name: "IsReply",
                table: "email_features",
                newName: "is_reply");

            migrationBuilder.RenameColumn(
                name: "IsReplied",
                table: "email_features",
                newName: "is_replied");

            migrationBuilder.RenameColumn(
                name: "IsInInbox",
                table: "email_features",
                newName: "is_in_inbox");

            migrationBuilder.RenameColumn(
                name: "IsImportant",
                table: "email_features",
                newName: "is_important");

            migrationBuilder.RenameColumn(
                name: "IsForwarded",
                table: "email_features",
                newName: "is_forwarded");

            migrationBuilder.RenameColumn(
                name: "IsArchived",
                table: "email_features",
                newName: "is_archived");

            migrationBuilder.RenameColumn(
                name: "InUserWhitelist",
                table: "email_features",
                newName: "in_user_whitelist");

            migrationBuilder.RenameColumn(
                name: "InUserBlacklist",
                table: "email_features",
                newName: "in_user_blacklist");

            migrationBuilder.RenameColumn(
                name: "ImageCount",
                table: "email_features",
                newName: "image_count");

            migrationBuilder.RenameColumn(
                name: "HourReceived",
                table: "email_features",
                newName: "hour_received");

            migrationBuilder.RenameColumn(
                name: "HasTrackingPixel",
                table: "email_features",
                newName: "has_tracking_pixel");

            migrationBuilder.RenameColumn(
                name: "HasListUnsubscribe",
                table: "email_features",
                newName: "has_list_unsubscribe");

            migrationBuilder.RenameColumn(
                name: "HasAttachments",
                table: "email_features",
                newName: "has_attachments");

            migrationBuilder.RenameColumn(
                name: "FeatureSchemaVersion",
                table: "email_features",
                newName: "feature_schema_version");

            migrationBuilder.RenameColumn(
                name: "ExtractedAt",
                table: "email_features",
                newName: "extracted_at");

            migrationBuilder.RenameColumn(
                name: "EmailSizeLog",
                table: "email_features",
                newName: "email_size_log");

            migrationBuilder.RenameColumn(
                name: "EmailAgeDays",
                table: "email_features",
                newName: "email_age_days");

            migrationBuilder.RenameColumn(
                name: "DmarcResult",
                table: "email_features",
                newName: "dmarc_result");

            migrationBuilder.RenameColumn(
                name: "DkimResult",
                table: "email_features",
                newName: "dkim_result");

            migrationBuilder.RenameColumn(
                name: "DayOfWeek",
                table: "email_features",
                newName: "day_of_week");

            migrationBuilder.RenameColumn(
                name: "ContactStrength",
                table: "email_features",
                newName: "contact_strength");

            migrationBuilder.RenameColumn(
                name: "BodyTextShort",
                table: "email_features",
                newName: "body_text_short");

            migrationBuilder.RenameColumn(
                name: "EmailId",
                table: "email_features",
                newName: "email_id");

            migrationBuilder.RenameColumn(
                name: "Snippet",
                table: "email_archive",
                newName: "snippet");

            migrationBuilder.RenameColumn(
                name: "UserCorrected",
                table: "email_archive",
                newName: "user_corrected");

            migrationBuilder.RenameColumn(
                name: "ThreadId",
                table: "email_archive",
                newName: "thread_id");

            migrationBuilder.RenameColumn(
                name: "SourceFolder",
                table: "email_archive",
                newName: "source_folder");

            migrationBuilder.RenameColumn(
                name: "SizeEstimate",
                table: "email_archive",
                newName: "size_estimate");

            migrationBuilder.RenameColumn(
                name: "ReceivedDate",
                table: "email_archive",
                newName: "received_date");

            migrationBuilder.RenameColumn(
                name: "ProviderType",
                table: "email_archive",
                newName: "provider_type");

            migrationBuilder.RenameColumn(
                name: "HeadersJson",
                table: "email_archive",
                newName: "headers_json");

            migrationBuilder.RenameColumn(
                name: "FolderTagsJson",
                table: "email_archive",
                newName: "folder_tags_json");

            migrationBuilder.RenameColumn(
                name: "BodyText",
                table: "email_archive",
                newName: "body_text");

            migrationBuilder.RenameColumn(
                name: "BodyHtml",
                table: "email_archive",
                newName: "body_html");

            migrationBuilder.RenameColumn(
                name: "ArchivedAt",
                table: "email_archive",
                newName: "archived_at");

            migrationBuilder.RenameColumn(
                name: "EmailId",
                table: "email_archive",
                newName: "email_id");

            migrationBuilder.AddForeignKey(
                name: "FK_label_associations_label_taxonomy_label_id",
                table: "label_associations",
                column: "label_id",
                principalTable: "label_taxonomy",
                principalColumn: "label_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_label_associations_training_emails_email_id",
                table: "label_associations",
                column: "email_id",
                principalTable: "training_emails",
                principalColumn: "email_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_label_associations_label_taxonomy_label_id",
                table: "label_associations");

            migrationBuilder.DropForeignKey(
                name: "FK_label_associations_training_emails_email_id",
                table: "label_associations");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "training_emails",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "thread_id",
                table: "training_emails",
                newName: "ThreadId");

            migrationBuilder.RenameColumn(
                name: "subject_prefix",
                table: "training_emails",
                newName: "SubjectPrefix");

            migrationBuilder.RenameColumn(
                name: "signal_confidence",
                table: "training_emails",
                newName: "SignalConfidence");

            migrationBuilder.RenameColumn(
                name: "raw_label_ids",
                table: "training_emails",
                newName: "RawLabelIds");

            migrationBuilder.RenameColumn(
                name: "last_seen_at",
                table: "training_emails",
                newName: "LastSeenAt");

            migrationBuilder.RenameColumn(
                name: "is_valid",
                table: "training_emails",
                newName: "IsValid");

            migrationBuilder.RenameColumn(
                name: "is_replied",
                table: "training_emails",
                newName: "IsReplied");

            migrationBuilder.RenameColumn(
                name: "is_read",
                table: "training_emails",
                newName: "IsRead");

            migrationBuilder.RenameColumn(
                name: "is_forwarded",
                table: "training_emails",
                newName: "IsForwarded");

            migrationBuilder.RenameColumn(
                name: "imported_at",
                table: "training_emails",
                newName: "ImportedAt");

            migrationBuilder.RenameColumn(
                name: "folder_origin",
                table: "training_emails",
                newName: "FolderOrigin");

            migrationBuilder.RenameColumn(
                name: "classification_signal",
                table: "training_emails",
                newName: "ClassificationSignal");

            migrationBuilder.RenameColumn(
                name: "account_id",
                table: "training_emails",
                newName: "AccountId");

            migrationBuilder.RenameColumn(
                name: "email_id",
                table: "training_emails",
                newName: "EmailId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "storage_quota",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_corrected_count",
                table: "storage_quota",
                newName: "UserCorrectedCount");

            migrationBuilder.RenameColumn(
                name: "limit_bytes",
                table: "storage_quota",
                newName: "LimitBytes");

            migrationBuilder.RenameColumn(
                name: "last_monitored_at",
                table: "storage_quota",
                newName: "LastMonitoredAt");

            migrationBuilder.RenameColumn(
                name: "last_cleanup_at",
                table: "storage_quota",
                newName: "LastCleanupAt");

            migrationBuilder.RenameColumn(
                name: "feature_count",
                table: "storage_quota",
                newName: "FeatureCount");

            migrationBuilder.RenameColumn(
                name: "feature_bytes",
                table: "storage_quota",
                newName: "FeatureBytes");

            migrationBuilder.RenameColumn(
                name: "current_bytes",
                table: "storage_quota",
                newName: "CurrentBytes");

            migrationBuilder.RenameColumn(
                name: "archive_count",
                table: "storage_quota",
                newName: "ArchiveCount");

            migrationBuilder.RenameColumn(
                name: "archive_bytes",
                table: "storage_quota",
                newName: "ArchiveBytes");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "scan_progress",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "scan_progress",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "scan_progress",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "total_estimate",
                table: "scan_progress",
                newName: "TotalEstimate");

            migrationBuilder.RenameColumn(
                name: "started_at",
                table: "scan_progress",
                newName: "StartedAt");

            migrationBuilder.RenameColumn(
                name: "scan_type",
                table: "scan_progress",
                newName: "ScanType");

            migrationBuilder.RenameColumn(
                name: "processed_count",
                table: "scan_progress",
                newName: "ProcessedCount");

            migrationBuilder.RenameColumn(
                name: "last_processed_email_id",
                table: "scan_progress",
                newName: "LastProcessedEmailId");

            migrationBuilder.RenameColumn(
                name: "history_id",
                table: "scan_progress",
                newName: "HistoryId");

            migrationBuilder.RenameColumn(
                name: "folder_progress_json",
                table: "scan_progress",
                newName: "FolderProgressJson");

            migrationBuilder.RenameColumn(
                name: "completed_at",
                table: "scan_progress",
                newName: "CompletedAt");

            migrationBuilder.RenameColumn(
                name: "account_id",
                table: "scan_progress",
                newName: "AccountId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "label_taxonomy",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "color",
                table: "label_taxonomy",
                newName: "Color");

            migrationBuilder.RenameColumn(
                name: "usage_count",
                table: "label_taxonomy",
                newName: "UsageCount");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "label_taxonomy",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "label_type",
                table: "label_taxonomy",
                newName: "LabelType");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "label_taxonomy",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "account_id",
                table: "label_taxonomy",
                newName: "AccountId");

            migrationBuilder.RenameColumn(
                name: "label_id",
                table: "label_taxonomy",
                newName: "LabelId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "label_associations",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "label_id",
                table: "label_associations",
                newName: "LabelId");

            migrationBuilder.RenameColumn(
                name: "is_training_signal",
                table: "label_associations",
                newName: "IsTrainingSignal");

            migrationBuilder.RenameColumn(
                name: "is_context_feature",
                table: "label_associations",
                newName: "IsContextFeature");

            migrationBuilder.RenameColumn(
                name: "email_id",
                table: "label_associations",
                newName: "EmailId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "label_associations",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "was_in_trash",
                table: "email_features",
                newName: "WasInTrash");

            migrationBuilder.RenameColumn(
                name: "was_in_spam",
                table: "email_features",
                newName: "WasInSpam");

            migrationBuilder.RenameColumn(
                name: "user_corrected",
                table: "email_features",
                newName: "UserCorrected");

            migrationBuilder.RenameColumn(
                name: "unsubscribe_link_in_body",
                table: "email_features",
                newName: "UnsubscribeLinkInBody");

            migrationBuilder.RenameColumn(
                name: "training_label",
                table: "email_features",
                newName: "TrainingLabel");

            migrationBuilder.RenameColumn(
                name: "topic_distribution_json",
                table: "email_features",
                newName: "TopicDistributionJson");

            migrationBuilder.RenameColumn(
                name: "topic_cluster_id",
                table: "email_features",
                newName: "TopicClusterId");

            migrationBuilder.RenameColumn(
                name: "thread_message_count",
                table: "email_features",
                newName: "ThreadMessageCount");

            migrationBuilder.RenameColumn(
                name: "subject_text",
                table: "email_features",
                newName: "SubjectText");

            migrationBuilder.RenameColumn(
                name: "subject_length",
                table: "email_features",
                newName: "SubjectLength");

            migrationBuilder.RenameColumn(
                name: "spf_result",
                table: "email_features",
                newName: "SpfResult");

            migrationBuilder.RenameColumn(
                name: "sender_known",
                table: "email_features",
                newName: "SenderKnown");

            migrationBuilder.RenameColumn(
                name: "sender_frequency",
                table: "email_features",
                newName: "SenderFrequency");

            migrationBuilder.RenameColumn(
                name: "sender_domain",
                table: "email_features",
                newName: "SenderDomain");

            migrationBuilder.RenameColumn(
                name: "sender_category",
                table: "email_features",
                newName: "SenderCategory");

            migrationBuilder.RenameColumn(
                name: "semantic_embedding_json",
                table: "email_features",
                newName: "SemanticEmbeddingJson");

            migrationBuilder.RenameColumn(
                name: "recipient_count",
                table: "email_features",
                newName: "RecipientCount");

            migrationBuilder.RenameColumn(
                name: "link_count",
                table: "email_features",
                newName: "LinkCount");

            migrationBuilder.RenameColumn(
                name: "label_count",
                table: "email_features",
                newName: "LabelCount");

            migrationBuilder.RenameColumn(
                name: "is_starred",
                table: "email_features",
                newName: "IsStarred");

            migrationBuilder.RenameColumn(
                name: "is_reply",
                table: "email_features",
                newName: "IsReply");

            migrationBuilder.RenameColumn(
                name: "is_replied",
                table: "email_features",
                newName: "IsReplied");

            migrationBuilder.RenameColumn(
                name: "is_in_inbox",
                table: "email_features",
                newName: "IsInInbox");

            migrationBuilder.RenameColumn(
                name: "is_important",
                table: "email_features",
                newName: "IsImportant");

            migrationBuilder.RenameColumn(
                name: "is_forwarded",
                table: "email_features",
                newName: "IsForwarded");

            migrationBuilder.RenameColumn(
                name: "is_archived",
                table: "email_features",
                newName: "IsArchived");

            migrationBuilder.RenameColumn(
                name: "in_user_whitelist",
                table: "email_features",
                newName: "InUserWhitelist");

            migrationBuilder.RenameColumn(
                name: "in_user_blacklist",
                table: "email_features",
                newName: "InUserBlacklist");

            migrationBuilder.RenameColumn(
                name: "image_count",
                table: "email_features",
                newName: "ImageCount");

            migrationBuilder.RenameColumn(
                name: "hour_received",
                table: "email_features",
                newName: "HourReceived");

            migrationBuilder.RenameColumn(
                name: "has_tracking_pixel",
                table: "email_features",
                newName: "HasTrackingPixel");

            migrationBuilder.RenameColumn(
                name: "has_list_unsubscribe",
                table: "email_features",
                newName: "HasListUnsubscribe");

            migrationBuilder.RenameColumn(
                name: "has_attachments",
                table: "email_features",
                newName: "HasAttachments");

            migrationBuilder.RenameColumn(
                name: "feature_schema_version",
                table: "email_features",
                newName: "FeatureSchemaVersion");

            migrationBuilder.RenameColumn(
                name: "extracted_at",
                table: "email_features",
                newName: "ExtractedAt");

            migrationBuilder.RenameColumn(
                name: "email_size_log",
                table: "email_features",
                newName: "EmailSizeLog");

            migrationBuilder.RenameColumn(
                name: "email_age_days",
                table: "email_features",
                newName: "EmailAgeDays");

            migrationBuilder.RenameColumn(
                name: "dmarc_result",
                table: "email_features",
                newName: "DmarcResult");

            migrationBuilder.RenameColumn(
                name: "dkim_result",
                table: "email_features",
                newName: "DkimResult");

            migrationBuilder.RenameColumn(
                name: "day_of_week",
                table: "email_features",
                newName: "DayOfWeek");

            migrationBuilder.RenameColumn(
                name: "contact_strength",
                table: "email_features",
                newName: "ContactStrength");

            migrationBuilder.RenameColumn(
                name: "body_text_short",
                table: "email_features",
                newName: "BodyTextShort");

            migrationBuilder.RenameColumn(
                name: "email_id",
                table: "email_features",
                newName: "EmailId");

            migrationBuilder.RenameColumn(
                name: "snippet",
                table: "email_archive",
                newName: "Snippet");

            migrationBuilder.RenameColumn(
                name: "user_corrected",
                table: "email_archive",
                newName: "UserCorrected");

            migrationBuilder.RenameColumn(
                name: "thread_id",
                table: "email_archive",
                newName: "ThreadId");

            migrationBuilder.RenameColumn(
                name: "source_folder",
                table: "email_archive",
                newName: "SourceFolder");

            migrationBuilder.RenameColumn(
                name: "size_estimate",
                table: "email_archive",
                newName: "SizeEstimate");

            migrationBuilder.RenameColumn(
                name: "received_date",
                table: "email_archive",
                newName: "ReceivedDate");

            migrationBuilder.RenameColumn(
                name: "provider_type",
                table: "email_archive",
                newName: "ProviderType");

            migrationBuilder.RenameColumn(
                name: "headers_json",
                table: "email_archive",
                newName: "HeadersJson");

            migrationBuilder.RenameColumn(
                name: "folder_tags_json",
                table: "email_archive",
                newName: "FolderTagsJson");

            migrationBuilder.RenameColumn(
                name: "body_text",
                table: "email_archive",
                newName: "BodyText");

            migrationBuilder.RenameColumn(
                name: "body_html",
                table: "email_archive",
                newName: "BodyHtml");

            migrationBuilder.RenameColumn(
                name: "archived_at",
                table: "email_archive",
                newName: "ArchivedAt");

            migrationBuilder.RenameColumn(
                name: "email_id",
                table: "email_archive",
                newName: "EmailId");

            migrationBuilder.AddForeignKey(
                name: "FK_label_associations_label_taxonomy_LabelId",
                table: "label_associations",
                column: "LabelId",
                principalTable: "label_taxonomy",
                principalColumn: "LabelId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_label_associations_training_emails_EmailId",
                table: "label_associations",
                column: "EmailId",
                principalTable: "training_emails",
                principalColumn: "EmailId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
