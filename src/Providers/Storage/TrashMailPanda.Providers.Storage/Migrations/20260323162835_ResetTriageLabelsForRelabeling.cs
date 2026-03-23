using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <inheritdoc />
    public partial class ResetTriageLabelsForRelabeling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-time reset: clear all triage decisions so the user can re-triage
            // with the new label set. Feature vectors are preserved for ML training;
            // only the human decisions are wiped. Does NOT affect scan_progress so
            // no full re-scan is triggered.
            migrationBuilder.Sql(
                "UPDATE email_features SET training_label = NULL, user_corrected = 0;");

            migrationBuilder.Sql(
                "UPDATE email_archive SET user_corrected = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Triage decisions cannot be restored after a reset — this migration
            // is intentionally irreversible.
        }
    }
}
