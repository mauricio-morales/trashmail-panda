using Microsoft.ML.Data;

namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// ML.NET IDataView row schema for action model training.
/// Mapped from EmailFeatureVector; Label is the action ground truth.
/// </summary>
public class ActionTrainingInput
{
    // Numeric features (directly usable by ML.NET)
    public float SenderKnown { get; set; }
    public float ContactStrength { get; set; }
    public float HasListUnsubscribe { get; set; }
    public float HasAttachments { get; set; }
    public float HourReceived { get; set; }
    public float DayOfWeek { get; set; }
    public float EmailSizeLog { get; set; }
    public float SubjectLength { get; set; }
    public float RecipientCount { get; set; }
    public float IsReply { get; set; }
    public float InUserWhitelist { get; set; }
    public float InUserBlacklist { get; set; }
    public float LabelCount { get; set; }
    public float LinkCount { get; set; }
    public float ImageCount { get; set; }
    public float HasTrackingPixel { get; set; }
    public float UnsubscribeLinkInBody { get; set; }
    public float EmailAgeDays { get; set; }
    public float IsInInbox { get; set; }
    public float IsStarred { get; set; }
    public float IsImportant { get; set; }
    public float WasInTrash { get; set; }
    public float WasInSpam { get; set; }
    public float IsArchived { get; set; }
    public float ThreadMessageCount { get; set; }
    public float SenderFrequency { get; set; }
    public float IsReplied { get; set; }
    public float IsForwarded { get; set; }

    // Categorical features (ML.NET will one-hot-encode these)
    public string SenderDomain { get; set; } = string.Empty;
    public string SpfResult { get; set; } = string.Empty;
    public string DkimResult { get; set; } = string.Empty;
    public string DmarcResult { get; set; } = string.Empty;

    // Text features (ML.NET will TF-IDF vectorise these)
    public string SubjectText { get; set; } = string.Empty;
    public string BodyTextShort { get; set; } = string.Empty;

    // Class balancing weight (inverse-frequency weight computed by FeaturePipelineBuilder)
    [ColumnName("Weight")]
    public float Weight { get; set; } = 1.0f;

    // Ground truth label ("Keep" | "Archive" | "Delete" | "Spam")
    [ColumnName("Label")]
    public string Label { get; set; } = string.Empty;
}
