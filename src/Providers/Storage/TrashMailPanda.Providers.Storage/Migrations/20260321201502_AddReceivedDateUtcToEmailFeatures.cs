using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivedDateUtcToEmailFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "received_date_utc",
                table: "email_features",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_email_features_received_date_utc",
                table: "email_features",
                column: "received_date_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_email_features_received_date_utc",
                table: "email_features");

            migrationBuilder.DropColumn(
                name: "received_date_utc",
                table: "email_features");
        }
    }
}
