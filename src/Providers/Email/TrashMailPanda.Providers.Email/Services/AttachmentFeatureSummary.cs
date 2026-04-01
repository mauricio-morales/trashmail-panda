namespace TrashMailPanda.Providers.Email.Services;

internal record AttachmentFeatureSummary(
    int Count,
    float TotalSizeLog,
    int HasDocuments,
    int HasImages,
    int HasAudio,
    int HasVideo,
    int HasXml,
    int HasBinaries,
    int HasOther)
{
    internal static AttachmentFeatureSummary Empty { get; } = new(0, 0f, 0, 0, 0, 0, 0, 0, 0);
}
