using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookings.Sql.Migrations
{
    /// <inheritdoc />
    public partial class DropBookingRefundAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amount",
                table: "BookingRefunds");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "BookingRefunds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "BookingRefunds",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "BookingRefunds",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }
    }
}
