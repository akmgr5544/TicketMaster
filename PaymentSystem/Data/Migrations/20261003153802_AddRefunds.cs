using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_PaymentOrderId_Type",
                table: "LedgerEntries");

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                table: "PaymentOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "LedgerEntries",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // Every entry written before refunds existed is a pay-in.
                defaultValue: "PayIn");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PaymentOrderId_Reason_Type",
                table: "LedgerEntries",
                columns: new[] { "PaymentOrderId", "Reason", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_PaymentOrderId_Reason_Type",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "RefundReference",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "LedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PaymentOrderId_Type",
                table: "LedgerEntries",
                columns: new[] { "PaymentOrderId", "Type" },
                unique: true);
        }
    }
}
