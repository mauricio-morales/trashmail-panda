using System.Threading.Tasks;
using Xunit;

namespace TrashMailPanda.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="TrashMailPanda.Services.RetentionEnforcementService"/>.
/// These tests require live Gmail OAuth credentials and will perform real Gmail API calls.
/// </summary>
[Trait("Category", "Integration")]
public class RetentionEnforcementIntegrationTests
{
    [Fact(Skip = "Requires OAuth - real Gmail credentials needed")]
    public async Task RunScanAsync_DeletesExpiredArchivedEmails_EndToEnd()
    {
        // 1. Seed email_features with expired rows (mock or staging Gmail)
        // 2. Call RetentionEnforcementService.RunScanAsync()
        // 3. Verify emails appear in Gmail TRASH
        // 4. Verify training_label is unchanged in email_features
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires OAuth - real Gmail credentials needed")]
    public async Task RunScanAsync_PersistsLastScanUtc_InConfigStore()
    {
        // 1. Run scan
        // 2. Call GetLastScanTimeAsync() and verify timestamp updated
        await Task.CompletedTask;
    }
}
