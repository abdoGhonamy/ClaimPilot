using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueChunkHashPerVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PolicyChunks_ContentHash",
                table: "PolicyChunks");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyChunks_PolicyVersionId_ContentHash",
                table: "PolicyChunks",
                columns: new[] { "PolicyVersionId", "ContentHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PolicyChunks_PolicyVersionId_ContentHash",
                table: "PolicyChunks");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyChunks_ContentHash",
                table: "PolicyChunks",
                column: "ContentHash",
                unique: true);
        }
    }
}
