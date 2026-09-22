using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace ClaimPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderIsolatedEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicyChunkEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Vector = table.Column<Vector>(type: "vector(768)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyChunkEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyChunkEmbeddings_PolicyChunks_PolicyChunkId",
                        column: x => x.PolicyChunkId,
                        principalTable: "PolicyChunks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyChunkEmbeddings_PolicyChunkId_Provider_Model",
                table: "PolicyChunkEmbeddings",
                columns: new[] { "PolicyChunkId", "Provider", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyChunkEmbeddings_Provider",
                table: "PolicyChunkEmbeddings",
                column: "Provider");

            // Preserve the old local index as an explicit Ollama pipeline. Gemini
            // embeddings are deliberately not synthesized: they must be generated
            // by Gemini before a Gemini run is allowed to retrieve them.
            migrationBuilder.Sql("""
                INSERT INTO "PolicyChunkEmbeddings" ("Id", "PolicyChunkId", "Provider", "Model", "Vector", "CreatedAt")
                SELECT md5("Id"::text || 'ollama')::uuid, "Id", 'ollama', 'nomic-embed-text', "Embedding", NOW()
                FROM "PolicyChunks"
                WHERE "Embedding" IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                CREATE INDEX "IX_PolicyChunkEmbeddings_Gemini_Vector_Hnsw"
                ON "PolicyChunkEmbeddings" USING hnsw ("Vector" vector_cosine_ops)
                WHERE "Provider" = 'gemini';
                """);
            migrationBuilder.Sql("""
                CREATE INDEX "IX_PolicyChunkEmbeddings_Ollama_Vector_Hnsw"
                ON "PolicyChunkEmbeddings" USING hnsw ("Vector" vector_cosine_ops)
                WHERE "Provider" = 'ollama';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PolicyChunkEmbeddings_Gemini_Vector_Hnsw\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PolicyChunkEmbeddings_Ollama_Vector_Hnsw\";");
            migrationBuilder.DropTable(
                name: "PolicyChunkEmbeddings");
        }
    }
}
