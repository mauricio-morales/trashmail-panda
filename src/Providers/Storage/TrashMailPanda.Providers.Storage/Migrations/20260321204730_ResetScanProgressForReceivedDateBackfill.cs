using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <summary>
    /// Data-only migration: resets scan progress so the next app launch triggers
    /// a full initial scan, which backfills ReceivedDateUtc on every email.
    /// TrainingLabel and UserCorrected are preserved by the upsert logic in
    /// EmailArchiveService.StoreFeatureAsync.
    /// </summary>
    public partial class ResetScanProgressForReceivedDateBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Delete all scan progress records so the app sees "never scanned"
            // and triggers a full initial scan on next launch.
            migrationBuilder.Sql("DELETE FROM scan_progress;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot restore deleted scan progress rows; a re-scan is harmless.
        }
    }
}
