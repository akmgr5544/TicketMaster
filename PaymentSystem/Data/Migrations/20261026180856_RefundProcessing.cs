using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefundProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingSince",
                table: "RefundOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "RefundOrders",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_RefundOrders_PspRefundReference",
                table: "RefundOrders",
                column: "PspRefundReference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefundOrders_PspRefundReference",
                table: "RefundOrders");

            migrationBuilder.DropColumn(
                name: "ProcessingSince",
                table: "RefundOrders");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "RefundOrders");
        }
    }
}
