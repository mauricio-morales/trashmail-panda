using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <summary>
    /// Data-only migration: clears the is_in_inbox flag on email_features rows
    /// whose authoritative folder (from training_emails.folder_origin) is SENT or DRAFT.
    ///
    /// Root cause: self-sent emails (To: yourself) receive both SENT and INBOX Gmail labels.
    /// BuildFeatureVector was setting is_in_inbox=1 from raw label IDs, causing those emails
    /// to appear in the triage queue even though they belong to SENT/DRAFT.
    /// </summary>
    public partial class FixSentEmailsInTriageQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE email_features
                SET is_in_inbox = 0
                WHERE email_id IN (
                    SELECT email_id FROM training_emails
                    WHERE folder_origin IN ('SENT', 'DRAFT')
                )
                AND is_in_inbox = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot determine which rows were previously 1 vs 0 — a re-scan will correct them.
        }
    }
}
