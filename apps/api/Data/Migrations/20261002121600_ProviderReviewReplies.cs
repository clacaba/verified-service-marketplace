using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProviderReviewReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderReviewReplies",
                columns: table => new
                {
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderReviewReplies", x => x.BookingId);
                    table.CheckConstraint("CK_ProviderReviewReply_Comment", "length(trim(\"Comment\")) >= 1 AND length(\"Comment\") <= 2000");
                    table.ForeignKey(
                        name: "FK_ProviderReviewReplies_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProviderReviewReplies_BookingReviews_BookingId",
                        column: x => x.BookingId,
                        principalTable: "BookingReviews",
                        principalColumn: "BookingId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderReviewReplies_ActorId",
                table: "ProviderReviewReplies",
                column: "ActorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderReviewReplies");
        }
    }
}
