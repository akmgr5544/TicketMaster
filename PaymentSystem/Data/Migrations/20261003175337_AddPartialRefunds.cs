using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPartialRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_PaymentOrderId_Reason_Type",
                table: "LedgerEntries");

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "PaymentOrders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundId",
                table: "LedgerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderRefunds",
                columns: table => new
                {
                    PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefunds", x => new { x.PaymentOrderId, x.RefundId });
                    table.ForeignKey(
                        name: "FK_OrderRefunds_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "PaymentOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            // Every refund before this one was a whole order's, made under the order's own id — the id a whole
            // refund still uses when Bookings names none — so a redelivery of one is recognised as already done.
            migrationBuilder.Sql("""
                INSERT INTO "OrderRefunds" ("PaymentOrderId", "RefundId", "Amount", "ProviderReference", "CreatedAt")
                SELECT "PaymentOrderId", "PaymentOrderId", "Amount", "RefundReference", "UpdatedAt"
                FROM "PaymentOrders"
                WHERE "Status" = 'Refunded' AND "RefundReference" IS NOT NULL;

                UPDATE "PaymentOrders" SET "RefundedAmount" = "Amount" WHERE "Status" = 'Refunded';

                UPDATE "LedgerEntries" SET "RefundId" = "PaymentOrderId" WHERE "Reason" = 'Refund';
                """);

            migrationBuilder.DropColumn(
                name: "RefundReference",
                table: "PaymentOrders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_RefundedAmount",
                table: "PaymentOrders",
                sql: "\"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"Amount\"");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PaymentOrderId_Reason_Type_RefundId",
                table: "LedgerEntries",
                columns: new[] { "PaymentOrderId", "Reason", "Type", "RefundId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderRefunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_RefundedAmount",
                table: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_PaymentOrderId_Reason_Type_RefundId",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "RefundId",
                table: "LedgerEntries");

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                table: "PaymentOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PaymentOrderId_Reason_Type",
                table: "LedgerEntries",
                columns: new[] { "PaymentOrderId", "Reason", "Type" },
                unique: true);
        }
    }
}
