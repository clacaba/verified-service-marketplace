using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompletedBookingReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingReviews",
                columns: table => new
                {
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingReviews", x => x.BookingId);
                    table.CheckConstraint("CK_BookingReview_Comment", "length(trim(\"Comment\")) >= 1 AND length(\"Comment\") <= 2000");
                    table.CheckConstraint("CK_BookingReview_Rating", "\"Rating\" >= 1 AND \"Rating\" <= 5");
                    table.CheckConstraint("CK_BookingReview_Status", "\"Status\" IN ('Published','Hidden')");
                    table.ForeignKey(
                        name: "FK_BookingReviews_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingReviews_Status_CreatedAt",
                table: "BookingReviews",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingReviews");
        }
    }
}
