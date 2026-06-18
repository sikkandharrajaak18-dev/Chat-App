using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat_App.Migrations
{
    /// <inheritdoc />
    public partial class RegisterCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "user",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "user",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NickName",
                table: "user",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Gender",
                table: "user");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "user");

            migrationBuilder.DropColumn(
                name: "NickName",
                table: "user");
        }
    }
}
