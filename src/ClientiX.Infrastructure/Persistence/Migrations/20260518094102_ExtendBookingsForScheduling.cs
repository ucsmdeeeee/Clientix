using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientiX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendBookingsForScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bookings_StartsAt",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_bookings_UserId_Status",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "ClientName",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "ClientPhone",
                table: "bookings");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "bookings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "bookings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "bookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientFirstName",
                table: "bookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientUsername",
                table: "bookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "idx_bookings_no_overlap",
                table: "bookings",
                columns: new[] { "UserId", "StartsAt" },
                unique: true,
                filter: "status IN ('pending', 'confirmed')");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_ClientTelegramId_StartsAt",
                table: "bookings",
                columns: new[] { "ClientTelegramId", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_bookings_no_overlap",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_bookings_ClientTelegramId_StartsAt",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "ClientFirstName",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "ClientUsername",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "bookings");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "bookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<string>(
                name: "ClientName",
                table: "bookings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientPhone",
                table: "bookings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_StartsAt",
                table: "bookings",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_UserId_Status",
                table: "bookings",
                columns: new[] { "UserId", "Status" });
        }
    }
}
