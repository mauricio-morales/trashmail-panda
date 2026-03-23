using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <inheritdoc />
    public partial class RenameArchiveThenDeleteLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename triage action labels stored in email_features.training_label
            // from internal kebab-case values to the user-facing "Archive for X" format.
            migrationBuilder.Sql(
                "UPDATE email_features SET training_label = 'Archive for 30d' WHERE training_label = 'archive-then-delete-30d';");

            migrationBuilder.Sql(
                "UPDATE email_features SET training_label = 'Archive for 1y' WHERE training_label = 'archive-then-delete-1y';");

            migrationBuilder.Sql(
                "UPDATE email_features SET training_label = 'Archive for 5y' WHERE training_label = 'archive-then-delete-5y';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE email_features SET training_label = 'archive-then-delete-30d' WHERE training_label = 'Archive for 30d';");

            migrationBuilder.Sql(
                "UPDATE email_features SET training_label = 'archive-then-delete-1y' WHERE training_label = 'Archive for 1y';");

            migrationBuilder.Sql(
                "UPDATE email_features SET training_label = 'archive-then-delete-5y' WHERE training_label = 'Archive for 5y';");
        }
    }
}
