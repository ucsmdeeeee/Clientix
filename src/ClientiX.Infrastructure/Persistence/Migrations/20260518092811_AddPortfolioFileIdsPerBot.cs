using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientiX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioFileIdsPerBot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Caption",
                table: "portfolio_items",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileIdsPerBot",
                table: "portfolio_items",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileIdsPerBot",
                table: "portfolio_items");

            migrationBuilder.AlterColumn<string>(
                name: "Caption",
                table: "portfolio_items",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);
        }
    }
}
