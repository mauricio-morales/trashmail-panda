using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentMlFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attachment_count",
                table: "email_features",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "has_audio_attachments",
                table: "email_features",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "has_binary_attachments",
                table: "email_features",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "has_doc_attachments",
                table: "email_features",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "has_image_attachments",
                table: "email_features",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "has_other_attachments",
                table: "email_features",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "has_video_attachments",
                table: "email_features",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "has_xml_attachments",
                table: "email_features",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "total_attachment_size_log",
                table: "email_features",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachment_count",
                table: "email_features");

            migrationBuilder.DropColumn(
                name: "has_audio_attachments",
                table: "email_features");

            migrationBuilder.DropColumn(
                name: "has_binary_attachments",
                table: "email_features");

            migrationBuilder.DropColumn(
                name: "has_doc_attachments",
                table: "email_features");

            migrationBuilder.DropColumn(
                name: "has_image_attachments",
                table: "email_features");

            migrationBuilder.DropColumn(
                name: "has_other_attachments",
                table: "email_features");

            migrationBuilder.DropColumn(
                name: "has_video_attachments",
                table: "email_features");

            migrationBuilder.DropColumn(
                name: "has_xml_attachments",
                table: "email_features");

            migrationBuilder.DropColumn(
                name: "total_attachment_size_log",
                table: "email_features");
        }
    }
}
