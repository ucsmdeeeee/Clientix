using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientiX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingHorizon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BookingHorizonDays",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 14);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingHorizonDays",
                table: "users");
        }
    }
}
