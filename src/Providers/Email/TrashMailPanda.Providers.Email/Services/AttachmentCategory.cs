namespace TrashMailPanda.Providers.Email.Services;

[Flags]
internal enum AttachmentCategory
{
    None = 0,
    Document = 1 << 0,
    Image = 1 << 1,
    Audio = 1 << 2,
    Video = 1 << 3,
    Xml = 1 << 4,
    Binary = 1 << 5,
    Other = 1 << 6
}
