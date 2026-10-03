using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequireCheckoutOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentEvents_OrderCount",
                table: "PaymentEvents",
                sql: "\"OrderCount\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentEvents_OrderCount",
                table: "PaymentEvents");
        }
    }
}
