using System;
using System.Collections.Generic;
using TrashMailPanda.Shared;

namespace TrashMailPanda.Providers.Email.Services;

internal static class AttachmentMimeClassifier
{
    private static readonly HashSet<string> XmlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/xml",
        "text/xml",
        "application/xhtml+xml",
        "application/atom+xml",
        "application/rss+xml",
        "application/soap+xml",
        "application/mathml+xml"
    };

    private static readonly HashSet<string> DocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint",
        "application/rtf",
        "text/richtext",
        "text/plain",
        "text/csv",
        "text/html"
    };

    private static readonly string[] DocumentPrefixes =
    [
        "application/vnd.openxmlformats-officedocument.",
        "application/vnd.oasis.opendocument.",
        "application/x-iwork-"
    ];

    private static readonly HashSet<string> BinaryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/zip",
        "application/x-zip-compressed",
        "application/x-rar-compressed",
        "application/x-tar",
        "application/gzip",
        "application/x-gzip",
        "application/x-7z-compressed",
        "application/x-bzip2",
        "application/x-bzip",
        "application/x-msdownload",
        "application/x-executable",
        "application/x-msdos-program",
        "application/x-apple-diskimage",
        "application/vnd.ms-cab-compressed"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".dll", ".dmg", ".iso", ".bin", ".deb", ".rpm",
        ".appimage", ".pkg", ".cab"
    };

    internal static AttachmentCategory Classify(string? mimeType, string? fileName = null)
    {
        var mime = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        var file = (fileName ?? string.Empty).Trim().ToLowerInvariant();

        if (mime.StartsWith("image/", StringComparison.Ordinal))
            return AttachmentCategory.Image;

        if (mime.StartsWith("audio/", StringComparison.Ordinal))
            return AttachmentCategory.Audio;

        if (mime.StartsWith("video/", StringComparison.Ordinal))
            return AttachmentCategory.Video;

        if (XmlTypes.Contains(mime))
            return AttachmentCategory.Xml;

        if (DocumentTypes.Contains(mime))
            return AttachmentCategory.Document;

        foreach (var prefix in DocumentPrefixes)
        {
            if (mime.StartsWith(prefix, StringComparison.Ordinal))
                return AttachmentCategory.Document;
        }

        if (BinaryTypes.Contains(mime))
            return AttachmentCategory.Binary;

        if (mime == "application/octet-stream")
        {
            if (!string.IsNullOrEmpty(file))
            {
                var dot = file.LastIndexOf('.');
                if (dot >= 0 && BinaryExtensions.Contains(file[dot..]))
                    return AttachmentCategory.Binary;
            }
            return AttachmentCategory.Other;
        }

        return AttachmentCategory.Other;
    }

    internal static AttachmentFeatureSummary Summarize(IReadOnlyList<EmailAttachment> attachments)
    {
        if (attachments.Count == 0)
            return AttachmentFeatureSummary.Empty;

        var count = 0;
        long totalBytes = 0;
        var hasDoc = false;
        var hasImage = false;
        var hasAudio = false;
        var hasVideo = false;
        var hasXml = false;
        var hasBinary = false;
        var hasOther = false;

        foreach (var attachment in attachments)
        {
            // Only count true attachments (non-inline parts with filename or attachmentId)
            // The CollectAttachments gate already handles this, but multi-part MIME containers
            // with a null/empty MIME type should be skipped.
            var mime = attachment.MimeType;
            if (string.IsNullOrEmpty(mime) || mime.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
                continue;

            count++;
            if (attachment.Size > 0)
                totalBytes += attachment.Size;

            var category = Classify(mime, attachment.FileName);

            hasDoc |= (category & AttachmentCategory.Document) != 0;
            hasImage |= (category & AttachmentCategory.Image) != 0;
            hasAudio |= (category & AttachmentCategory.Audio) != 0;
            hasVideo |= (category & AttachmentCategory.Video) != 0;
            hasXml |= (category & AttachmentCategory.Xml) != 0;
            hasBinary |= (category & AttachmentCategory.Binary) != 0;
            hasOther |= (category & AttachmentCategory.Other) != 0;
        }

        return new AttachmentFeatureSummary(
            Count: count,
            TotalSizeLog: ComputeSizeLog(totalBytes),
            HasDocuments: hasDoc ? 1 : 0,
            HasImages: hasImage ? 1 : 0,
            HasAudio: hasAudio ? 1 : 0,
            HasVideo: hasVideo ? 1 : 0,
            HasXml: hasXml ? 1 : 0,
            HasBinaries: hasBinary ? 1 : 0,
            HasOther: hasOther ? 1 : 0);
    }

    internal static float ComputeSizeLog(long totalBytes) =>
        (float)Math.Log10((double)totalBytes + 1.0);
}
