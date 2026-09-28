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
            // CodeRabbit #96: deployment-safe swap. CONCURRENTLY cannot run in
            // a transaction (suppressTransaction) and doesn't block writes while
            // building. Dropping the temp name first makes a rerun after a
            // partial failure (e.g. an INVALID leftover index) idempotent, and
            // the final rename is metadata-only.
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS "IX_TextbookChunks_Embedding_hnsw_new"
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_TextbookChunks_Embedding_hnsw_new"
                ON "TextbookChunks" USING hnsw ("Embedding" vector_cosine_ops)
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS "IX_TextbookChunks_Embedding"
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                ALTER INDEX "IX_TextbookChunks_Embedding_hnsw_new"
                RENAME TO "IX_TextbookChunks_Embedding"
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // same dance back to the original ivfflat index
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS "IX_TextbookChunks_Embedding_ivfflat_new"
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_TextbookChunks_Embedding_ivfflat_new"
                ON "TextbookChunks" USING ivfflat ("Embedding" vector_cosine_ops)
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS "IX_TextbookChunks_Embedding"
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                ALTER INDEX "IX_TextbookChunks_Embedding_ivfflat_new"
                RENAME TO "IX_TextbookChunks_Embedding"
                """,
                suppressTransaction: true);
        }
    }
}
