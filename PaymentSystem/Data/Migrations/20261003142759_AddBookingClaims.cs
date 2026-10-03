using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingClaims",
                columns: table => new
                {
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingClaims", x => x.BookingId);
                });

            // Every checkout already stored was opened by a request, so it holds the booking's claim.
            migrationBuilder.Sql("""
                INSERT INTO "BookingClaims" ("BookingId", "Status", "CreatedAt")
                SELECT "BookingId", 'Requested', "CreatedAt" FROM "PaymentEvents";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingClaims");
        }
    }
}
