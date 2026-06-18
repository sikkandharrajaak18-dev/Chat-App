using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat_App.Migrations
{
    /// <inheritdoc />
    public partial class Duration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Duration",
                table: "message",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Duration",
                table: "groupMessage",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Duration",
                table: "message");

            migrationBuilder.DropColumn(
                name: "Duration",
                table: "groupMessage");
        }
    }
}
