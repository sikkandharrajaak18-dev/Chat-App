using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat_App.Migrations
{
    /// <inheritdoc />
    public partial class Backgroundfit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundFit",
                table: "chatBackground",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundFit",
                table: "chatBackground");
        }
    }
}
