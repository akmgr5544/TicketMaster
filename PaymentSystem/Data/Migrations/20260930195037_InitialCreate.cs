using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    CheckoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPaymentDone = table.Column<bool>(type: "boolean", nullable: false),
                    OrderCount = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.CheckoutId);
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.WalletId);
                });

            migrationBuilder.CreateTable(
                name: "PaymentOrders",
                columns: table => new
                {
                    PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PspToken = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WalletUpdated = table.Column<bool>(type: "boolean", nullable: false),
                    LedgerUpdated = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOrders", x => x.PaymentOrderId);
                    table.ForeignKey(
                        name: "FK_PaymentOrders_PaymentEvents_CheckoutId",
                        column: x => x.CheckoutId,
                        principalTable: "PaymentEvents",
                        principalColumn: "CheckoutId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "PaymentOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PaymentOrderId_Type",
                table: "LedgerEntries",
                columns: new[] { "PaymentOrderId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_BookingId",
                table: "PaymentEvents",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_CheckoutId_MerchantId",
                table: "PaymentOrders",
                columns: new[] { "CheckoutId", "MerchantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_OwnerId_Currency",
                table: "Wallets",
                columns: new[] { "OwnerId", "Currency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "Wallets");

            migrationBuilder.DropTable(
                name: "PaymentOrders");

            migrationBuilder.DropTable(
                name: "PaymentEvents");
        }
    }
}
