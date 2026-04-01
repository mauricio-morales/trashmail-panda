using Google.Apis.Gmail.v1.Data;
using TrashMailPanda.Providers.Email.Services;
using TrashMailPanda.Shared;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Email;

[Trait("Category", "Unit")]
public class AttachmentMimeClassifierTests
{
    // ============================================================
    // Classify — explicit MIME types per category
    // ============================================================

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    [InlineData("image/svg+xml")]
    public void Classify_ImageMimeTypes_ReturnsImage(string mime)
    {
        var result = AttachmentMimeClassifier.Classify(mime);
        Assert.Equal(AttachmentCategory.Image, result);
    }

    [Theory]
    [InlineData("audio/mpeg")]
    [InlineData("audio/ogg")]
    [InlineData("audio/wav")]
    [InlineData("audio/x-flac")]
    public void Classify_AudioMimeTypes_ReturnsAudio(string mime)
    {
        var result = AttachmentMimeClassifier.Classify(mime);
        Assert.Equal(AttachmentCategory.Audio, result);
    }

    [Theory]
    [InlineData("video/mp4")]
    [InlineData("video/quicktime")]
    [InlineData("video/x-msvideo")]
    [InlineData("video/webm")]
    public void Classify_VideoMimeTypes_ReturnsVideo(string mime)
    {
        var result = AttachmentMimeClassifier.Classify(mime);
        Assert.Equal(AttachmentCategory.Video, result);
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("application/xhtml+xml")]
    [InlineData("application/atom+xml")]
    [InlineData("application/rss+xml")]
    [InlineData("application/soap+xml")]
    [InlineData("application/mathml+xml")]
    public void Classify_XmlMimeTypes_ReturnsXml(string mime)
    {
        var result = AttachmentMimeClassifier.Classify(mime);
        Assert.Equal(AttachmentCategory.Xml, result);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/msword")]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("application/vnd.ms-powerpoint")]
    [InlineData("application/rtf")]
    [InlineData("text/richtext")]
    [InlineData("text/plain")]
    [InlineData("text/csv")]
    [InlineData("text/html")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("application/vnd.oasis.opendocument.text")]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet")]
    [InlineData("application/x-iwork-pages-sffpages")]
    public void Classify_DocumentMimeTypes_ReturnsDocument(string mime)
    {
        var result = AttachmentMimeClassifier.Classify(mime);
        Assert.Equal(AttachmentCategory.Document, result);
    }

    [Theory]
    [InlineData("application/zip")]
    [InlineData("application/x-zip-compressed")]
    [InlineData("application/x-rar-compressed")]
    [InlineData("application/x-tar")]
    [InlineData("application/gzip")]
    [InlineData("application/x-gzip")]
    [InlineData("application/x-7z-compressed")]
    [InlineData("application/x-bzip2")]
    [InlineData("application/x-bzip")]
    [InlineData("application/x-msdownload")]
    [InlineData("application/x-executable")]
    [InlineData("application/x-msdos-program")]
    [InlineData("application/x-apple-diskimage")]
    [InlineData("application/vnd.ms-cab-compressed")]
    public void Classify_BinaryMimeTypes_ReturnsBinary(string mime)
    {
        var result = AttachmentMimeClassifier.Classify(mime);
        Assert.Equal(AttachmentCategory.Binary, result);
    }

    [Theory]
    [InlineData("application/octet-stream", "file.exe")]
    [InlineData("application/octet-stream", "setup.msi")]
    [InlineData("application/octet-stream", "installer.dmg")]
    [InlineData("application/octet-stream", "disk.iso")]
    [InlineData("application/octet-stream", "data.bin")]
    [InlineData("application/octet-stream", "library.dll")]
    [InlineData("application/octet-stream", "package.deb")]
    [InlineData("application/octet-stream", "package.rpm")]
    [InlineData("application/octet-stream", "app.appimage")]
    [InlineData("application/octet-stream", "installer.pkg")]
    [InlineData("application/octet-stream", "archive.cab")]
    public void Classify_OctetStreamWithBinaryExtension_ReturnsBinary(string mime, string fileName)
    {
        var result = AttachmentMimeClassifier.Classify(mime, fileName);
        Assert.Equal(AttachmentCategory.Binary, result);
    }

    [Fact]
    public void Classify_OctetStreamWithoutBinaryExtension_ReturnsOther()
    {
        var result = AttachmentMimeClassifier.Classify("application/octet-stream", "data.xyz");
        Assert.Equal(AttachmentCategory.Other, result);
    }

    [Fact]
    public void Classify_OctetStreamWithNoFileName_ReturnsOther()
    {
        var result = AttachmentMimeClassifier.Classify("application/octet-stream");
        Assert.Equal(AttachmentCategory.Other, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_NullOrEmptyMimeType_ReturnsOther(string? mime)
    {
        var result = AttachmentMimeClassifier.Classify(mime);
        Assert.Equal(AttachmentCategory.Other, result);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("message/rfc822")]
    [InlineData("application/x-unknown")]
    [InlineData("text/calendar")]
    public void Classify_UnknownMimeTypes_ReturnsOther(string mime)
    {
        var result = AttachmentMimeClassifier.Classify(mime);
        Assert.Equal(AttachmentCategory.Other, result);
    }

    [Theory]
    [InlineData("multipart/mixed")]
    [InlineData("multipart/alternative")]
    [InlineData("multipart/related")]
    public void Classify_MultipartContainers_ReturnsOther(string mime)
    {
        // Multipart containers are structural MIME wrappers; not classified as attachments.
        // CollectAttachments excludes them; Classify returns Other for any that slip through.
        var result = AttachmentMimeClassifier.Classify(mime);
        Assert.Equal(AttachmentCategory.Other, result);
    }

    // ============================================================
    // Summarize — aggregate a list of attachments
    // ============================================================

    [Fact]
    public void Summarize_EmptyList_ReturnsEmpty()
    {
        var result = AttachmentMimeClassifier.Summarize(Array.Empty<EmailAttachment>());
        Assert.Equal(AttachmentFeatureSummary.Empty, result);
        Assert.Equal(0, result.Count);
        Assert.Equal(0f, result.TotalSizeLog);
    }

    [Fact]
    public void Summarize_SinglePdfAttachment_HasDocFlag()
    {
        var attachments = new List<EmailAttachment>
        {
            new() { FileName = "report.pdf", MimeType = "application/pdf", Size = 1000 }
        };

        var result = AttachmentMimeClassifier.Summarize(attachments);

        Assert.Equal(1, result.Count);
        Assert.Equal(1, result.HasDocuments);
        Assert.Equal(0, result.HasImages);
        Assert.Equal(0, result.HasAudio);
        Assert.Equal(0, result.HasVideo);
        Assert.Equal(0, result.HasXml);
        Assert.Equal(0, result.HasBinaries);
        Assert.Equal(0, result.HasOther);
        Assert.Equal(AttachmentMimeClassifier.ComputeSizeLog(1000), result.TotalSizeLog);
    }

    [Fact]
    public void Summarize_SingleImageAttachment_HasImageFlag()
    {
        var attachments = new List<EmailAttachment>
        {
            new() { FileName = "photo.jpg", MimeType = "image/jpeg", Size = 500 }
        };

        var result = AttachmentMimeClassifier.Summarize(attachments);

        Assert.Equal(1, result.Count);
        Assert.Equal(0, result.HasDocuments);
        Assert.Equal(1, result.HasImages);
    }

    [Fact]
    public void Summarize_SingleAudioAttachment_HasAudioFlag()
    {
        var attachments = new List<EmailAttachment>
        {
            new() { FileName = "song.mp3", MimeType = "audio/mpeg", Size = 5000 }
        };

        var result = AttachmentMimeClassifier.Summarize(attachments);
        Assert.Equal(1, result.HasAudio);
    }

    [Fact]
    public void Summarize_SingleVideoAttachment_HasVideoFlag()
    {
        var attachments = new List<EmailAttachment>
        {
            new() { FileName = "clip.mp4", MimeType = "video/mp4", Size = 50000 }
        };

        var result = AttachmentMimeClassifier.Summarize(attachments);
        Assert.Equal(1, result.HasVideo);
    }

    [Fact]
    public void Summarize_SingleXmlAttachment_HasXmlFlag()
    {
        var attachments = new List<EmailAttachment>
        {
            new() { FileName = "feed.xml", MimeType = "application/xml", Size = 200 }
        };

        var result = AttachmentMimeClassifier.Summarize(attachments);
        Assert.Equal(1, result.HasXml);
    }

    [Fact]
    public void Summarize_SingleBinaryAttachment_HasBinaryFlag()
    {
        var attachments = new List<EmailAttachment>
        {
            new() { FileName = "archive.zip", MimeType = "application/zip", Size = 100000 }
        };

        var result = AttachmentMimeClassifier.Summarize(attachments);
        Assert.Equal(1, result.HasBinaries);
    }

    [Fact]
    public void Summarize_SingleOtherAttachment_HasOtherFlag()
    {
        var attachments = new List<EmailAttachment>
        {
            new() { FileName = "data.json", MimeType = "application/json", Size = 300 }
        };

        var result = AttachmentMimeClassifier.Summarize(attachments);
        Assert.Equal(1, result.HasOther);
    }

    [Fact]
    public void Summarize_MixedTypes_SetsMultipleFlags()
    {
        var attachments = new List<EmailAttachment>
        {
            new() { FileName = "report.pdf",   MimeType = "application/pdf",  Size = 1000 },
            new() { FileName = "photo.png",    MimeType = "image/png",        Size = 2000 },
            new() { FileName = "archive.zip",  MimeType = "application/zip",  Size = 5000 }
        };

        var result = AttachmentMimeClassifier.Summarize(attachments);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result.HasDocuments);
        Assert.Equal(1, result.HasImages);
        Assert.Equal(0, result.HasAudio);
        Assert.Equal(0, result.HasVideo);
        Assert.Equal(0, result.HasXml);
        Assert.Equal(1, result.HasBinaries);
        Assert.Equal(0, result.HasOther);
        Assert.Equal(AttachmentMimeClassifier.ComputeSizeLog(1000 + 2000 + 5000), result.TotalSizeLog);
    }

    [Fact]
    public void Summarize_AttachmentWithZeroSize_ExcludedFromSizeTotal()
    {
        var attachments = new List<EmailAttachment>
        {
            new() { FileName = "doc.pdf", MimeType = "application/pdf", Size = 0 }
        };

        var result = AttachmentMimeClassifier.Summarize(attachments);

        Assert.Equal(1, result.Count);
        Assert.Equal(1, result.HasDocuments);
        // Size 0 → ComputeSizeLog(0) = log10(1) = 0
        Assert.Equal(0f, result.TotalSizeLog);
    }

    [Fact]
    public void Summarize_MultipartContainerAttachment_IsExcluded()
    {
        // multipart/mixed parts are structural containers and should not be counted
        var attachments = new List<EmailAttachment>
        {
            new() { FileName = string.Empty, MimeType = "multipart/mixed", Size = 0 }
        };

        var result = AttachmentMimeClassifier.Summarize(attachments);

        Assert.Equal(AttachmentFeatureSummary.Empty, result);
    }

    // ============================================================
    // ComputeSizeLog
    // ============================================================

    [Fact]
    public void ComputeSizeLog_Zero_ReturnsZero()
    {
        var result = AttachmentMimeClassifier.ComputeSizeLog(0);
        Assert.Equal(0f, result, precision: 5);
    }

    [Fact]
    public void ComputeSizeLog_OneThousand_ReturnsApproxThree()
    {
        // log10(1000 + 1) ≈ 3.0004
        var result = AttachmentMimeClassifier.ComputeSizeLog(1000);
        Assert.True(result > 3f && result < 3.01f);
    }

    [Fact]
    public void ComputeSizeLog_OneMillionBytes_ReturnsApproxSix()
    {
        // log10(1_000_000 + 1) ≈ 6.0000
        var result = AttachmentMimeClassifier.ComputeSizeLog(1_000_000);
        Assert.True(result > 5.99f && result < 6.01f);
    }

    [Fact]
    public void ComputeSizeLog_MaxLong_ReturnsFiniteFloat()
    {
        var result = AttachmentMimeClassifier.ComputeSizeLog(long.MaxValue);
        Assert.True(float.IsFinite(result));
        Assert.True(result > 0f);
    }
}
