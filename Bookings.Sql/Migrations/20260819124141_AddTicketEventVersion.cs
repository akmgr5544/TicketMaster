using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookings.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketEventVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "EventVersion",
                table: "Tickets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventVersion",
                table: "Tickets");
        }
    }
}
