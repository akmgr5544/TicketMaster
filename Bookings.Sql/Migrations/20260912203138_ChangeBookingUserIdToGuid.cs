using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookings.Sql.Migrations
{
    /// <inheritdoc />
    public partial class ChangeBookingUserIdToGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Postgres will not implicitly cast text to uuid, so the scaffolded AlterColumn would fail
            // on any populated table. Spelled out with USING: harmless on the empty table this ships
            // against, correct if a row ever existed.
            migrationBuilder.Sql(
                @"ALTER TABLE ""Bookings"" ALTER COLUMN ""UserId"" TYPE uuid USING (""UserId""::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"ALTER TABLE ""Bookings"" ALTER COLUMN ""UserId"" TYPE text USING (""UserId""::text);");
        }
    }
}
