using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegenerateSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ponytail: idempotent — index may already exist from prior migration's raw SQL
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_class c
                        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'public' AND c.relname = 'IX_CodeSubmissions_CodeEmbedding'
                    ) THEN
                        CREATE INDEX "IX_CodeSubmissions_CodeEmbedding"
                        ON "CodeSubmissions" USING hnsw ("CodeEmbedding" vector_cosine_ops);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_CodeSubmissions_CodeEmbedding";
                """);
        }
    }
}
