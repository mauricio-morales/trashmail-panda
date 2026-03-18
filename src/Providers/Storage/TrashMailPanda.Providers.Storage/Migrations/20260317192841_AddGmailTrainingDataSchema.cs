using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailTrainingDataSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IsForwarded",
                table: "email_features",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IsReplied",
                table: "email_features",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "label_taxonomy",
                columns: table => new
                {
                    LabelId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    LabelType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    UsageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_taxonomy", x => x.LabelId);
                });

            migrationBuilder.CreateTable(
                name: "scan_progress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    ScanType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FolderProgressJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    HistoryId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    ProcessedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalEstimate = table.Column<int>(type: "INTEGER", nullable: true),
                    LastProcessedEmailId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_progress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "training_emails",
                columns: table => new
                {
                    EmailId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    ThreadId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FolderOrigin = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReplied = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsForwarded = table.Column<bool>(type: "INTEGER", nullable: false),
                    SubjectPrefix = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    ClassificationSignal = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SignalConfidence = table.Column<float>(type: "REAL", nullable: false),
                    IsValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    RawLabelIds = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_emails", x => x.EmailId);
                });

            migrationBuilder.CreateTable(
                name: "label_associations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmailId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    LabelId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsTrainingSignal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsContextFeature = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_associations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_label_associations_label_taxonomy_LabelId",
                        column: x => x.LabelId,
                        principalTable: "label_taxonomy",
                        principalColumn: "LabelId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_label_associations_training_emails_EmailId",
                        column: x => x.EmailId,
                        principalTable: "training_emails",
                        principalColumn: "EmailId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_label_assoc_email",
                table: "label_associations",
                column: "EmailId");

            migrationBuilder.CreateIndex(
                name: "idx_label_assoc_label",
                table: "label_associations",
                columns: new[] { "LabelId", "IsTrainingSignal" });

            migrationBuilder.CreateIndex(
                name: "idx_label_assoc_unique",
                table: "label_associations",
                columns: new[] { "EmailId", "LabelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_label_taxonomy_account",
                table: "label_taxonomy",
                columns: new[] { "AccountId", "LabelType" });

            migrationBuilder.CreateIndex(
                name: "idx_label_taxonomy_usage",
                table: "label_taxonomy",
                column: "UsageCount");

            migrationBuilder.CreateIndex(
                name: "idx_scan_progress_account_status",
                table: "scan_progress",
                columns: new[] { "AccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "idx_training_emails_account",
                table: "training_emails",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "idx_training_emails_last_seen",
                table: "training_emails",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "idx_training_emails_signal",
                table: "training_emails",
                columns: new[] { "ClassificationSignal", "IsValid" });

            migrationBuilder.CreateIndex(
                name: "idx_training_emails_thread",
                table: "training_emails",
                column: "ThreadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "label_associations");

            migrationBuilder.DropTable(
                name: "scan_progress");

            migrationBuilder.DropTable(
                name: "label_taxonomy");

            migrationBuilder.DropTable(
                name: "training_emails");

            migrationBuilder.DropColumn(
                name: "IsForwarded",
                table: "email_features");

            migrationBuilder.DropColumn(
                name: "IsReplied",
                table: "email_features");
        }
    }
}
