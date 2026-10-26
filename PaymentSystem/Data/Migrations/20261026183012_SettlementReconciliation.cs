using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class SettlementReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SucceededAt",
                table: "RefundOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SettlementDiscrepancies",
                columns: table => new
                {
                    SettlementDiscrepancyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefundOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ExpectedCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ActualAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ActualCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FoundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementDiscrepancies", x => x.SettlementDiscrepancyId);
                });

            migrationBuilder.CreateTable(
                name: "SettlementRecords",
                columns: table => new
                {
                    SettlementRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementRecords", x => x.SettlementRecordId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementDiscrepancies_Provider_Kind_Reference",
                table: "SettlementDiscrepancies",
                columns: new[] { "Provider", "Kind", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementRecords_Provider_ProviderReference_Kind",
                table: "SettlementRecords",
                columns: new[] { "Provider", "ProviderReference", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SettlementDiscrepancies");

            migrationBuilder.DropTable(
                name: "SettlementRecords");

            migrationBuilder.DropColumn(
                name: "SucceededAt",
                table: "RefundOrders");
        }
    }
}
