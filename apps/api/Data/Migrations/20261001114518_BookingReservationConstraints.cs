using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BookingReservationConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Booking_Interval",
                table: "Bookings",
                sql: "\"StartsAt\" < \"EndsAt\" AND \"ReservedFrom\" <= \"StartsAt\" AND \"ReservedUntil\" >= \"EndsAt\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Booking_Status",
                table: "Bookings",
                sql: "\"Status\" IN ('PendingProviderConfirmation','Confirmed','InProgress','Completed','Declined','Expired','CancelledByCustomer','CancelledByProvider')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Booking_Interval",
                table: "Bookings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Booking_Status",
                table: "Bookings");
        }
    }
}
