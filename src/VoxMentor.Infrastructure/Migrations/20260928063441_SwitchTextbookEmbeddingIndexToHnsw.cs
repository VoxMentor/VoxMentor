using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SwitchTextbookEmbeddingIndexToHnsw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TextbookChunks_Embedding",
                table: "TextbookChunks");

            migrationBuilder.CreateIndex(
                name: "IX_TextbookChunks_Embedding",
                table: "TextbookChunks",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TextbookChunks_Embedding",
                table: "TextbookChunks");

            migrationBuilder.CreateIndex(
                name: "IX_TextbookChunks_Embedding",
                table: "TextbookChunks",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "ivfflat")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }
    }
}
