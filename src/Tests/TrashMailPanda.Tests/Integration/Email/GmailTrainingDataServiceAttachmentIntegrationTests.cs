using System.Threading.Tasks;
using Xunit;

namespace TrashMailPanda.Tests.Integration.Email;

/// <summary>
/// Integration test skeleton for attachment feature extraction via incremental sync.
/// Requires live Gmail OAuth credentials — skipped in CI by default.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GmailTrainingDataServiceAttachmentIntegrationTests
{
    /// <summary>
    /// Verifies that after an incremental sync the feature store rows include
    /// populated attachment columns for at least 10 emails.
    /// </summary>
    [Fact(Skip = "Requires OAuth - set GMAIL_CLIENT_ID/SECRET env vars")]
    public async Task IncrementalSync_PopulatesAttachmentColumnsForEmails()
    {
        // Arrange: configure GmailEmailProvider and GmailTrainingDataService using
        // GMAIL_CLIENT_ID / GMAIL_CLIENT_SECRET environment variables, then run
        // IncrementalSyncAsync("me", ...) against a live Gmail account.
        //
        // Assert:
        //   - At least 10 rows returned from GetAllFeaturesAsync(FeatureSchema.CurrentVersion)
        //   - Every row has FeatureSchemaVersion == FeatureSchema.CurrentVersion (== 2)
        //   - Rows with counted attachments have AttachmentCount > 0 and
        //     at least one HasDoc/Image/Audio/Video/Xml/Binary/OtherAttachments flag set
        //
        // See docs/oauth/GMAIL_OAUTH_CONSOLE_SETUP.md for credential setup instructions.
        await Task.CompletedTask; // placeholder — replace with real implementation when running locally
    }
}
