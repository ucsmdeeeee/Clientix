using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientiX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Europe/Moscow");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "users");
        }
    }
}
