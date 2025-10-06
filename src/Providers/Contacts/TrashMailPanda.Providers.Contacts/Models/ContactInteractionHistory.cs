namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Historical interaction data for trust signal calculation
/// </summary>
public class ContactInteractionHistory
{
    public string ContactId { get; set; } = string.Empty;
    public int EmailCount { get; set; }
    public int SentEmailCount { get; set; }
    public int ReceivedEmailCount { get; set; }
    public DateTime? LastInteractionDate { get; set; }
    public DateTime? FirstInteractionDate { get; set; }
    public List<string> InteractionTypes { get; set; } = new();
}