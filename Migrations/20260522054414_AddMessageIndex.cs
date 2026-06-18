using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat_App.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_message_SenderId_ReceiverId_SentAt",
                table: "message",
                columns: new[] { "SenderId", "ReceiverId", "SentAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_message_SenderId_ReceiverId_SentAt",
                table: "message");
        }
    }
}
