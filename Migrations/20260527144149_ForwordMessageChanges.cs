using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat_App.Migrations
{
    /// <inheritdoc />
    public partial class ForwordMessageChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OriginalSenderId",
                table: "message",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalMessageId",
                table: "groupMessage",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalSenderId",
                table: "message");

            migrationBuilder.DropColumn(
                name: "OriginalMessageId",
                table: "groupMessage");
        }
    }
}
