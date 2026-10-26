using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Data.Migrations
{
    /// <inheritdoc />
    public partial class OneVoidedEmailPerBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Notifications_BookingId_Voided",
                table: "Notifications",
                column: "BookingId",
                unique: true,
                filter: "\"Kind\" IN ('PaymentFailed', 'BookingCancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_BookingId_Voided",
                table: "Notifications");
        }
    }
}
