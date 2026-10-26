using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class OneOrderPerCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_CheckoutId_SellerId",
                table: "PaymentOrders");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_CheckoutId",
                table: "PaymentOrders",
                column: "CheckoutId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_CheckoutId",
                table: "PaymentOrders");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_CheckoutId_SellerId",
                table: "PaymentOrders",
                columns: new[] { "CheckoutId", "SellerId" },
                unique: true);
        }
    }
}
