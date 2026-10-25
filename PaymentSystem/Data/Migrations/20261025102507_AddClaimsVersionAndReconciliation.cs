using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimsVersionAndReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_CheckoutId",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "PaymentOrders");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutingSince",
                table: "PaymentOrders",
                type: "timestamp with time zone",
                nullable: true);

            // Edited by hand: the generated AlterColumn has no USING, and Postgres will not cast varchar to uuid
            // implicitly, even on an empty table.
            migrationBuilder.Sql("ALTER TABLE \"PaymentMethods\" ALTER COLUMN \"UserId\" TYPE uuid USING \"UserId\"::uuid;");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "PaymentEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_CheckoutId_SellerId",
                table: "PaymentOrders",
                columns: new[] { "CheckoutId", "SellerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_Status_ExecutingSince",
                table: "PaymentOrders",
                columns: new[] { "Status", "ExecutingSince" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingClaims");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_CheckoutId_SellerId",
                table: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_Status_ExecutingSince",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "ExecutingSince",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PaymentEvents");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "PaymentOrders",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.Sql("ALTER TABLE \"PaymentMethods\" ALTER COLUMN \"UserId\" TYPE character varying(64) USING \"UserId\"::text;");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_CheckoutId",
                table: "PaymentOrders",
                column: "CheckoutId");
        }
    }
}
