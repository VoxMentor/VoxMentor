using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LegacyTextbookJobTransition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy transition + duplicate reconciliation (#104 CodeRabbit).
            // Rows predating content-hash tracking keep ContentHash = '' forever
            // (their staged files may be gone — a backfill is impossible), so
            // they can never match a retry. Pending ones are handed an explicit
            // end state; Processing ones are left to their worker, and Completed
            // ones are left ingested (a retry of that content re-ingests once —
            // accepted one-off cost, see docs/API.md textbook section).
            // The earlier migration only had a NON-unique index, so concurrent
            // identical uploads could have written duplicate reusable rows;
            // reconcile them (and their chunks — TextbookChunks has no FK)
            // before the unique index is created. LOCK TABLE is the write gate:
            // migrations run at startup before this instance serves traffic or
            // starts its Hangfire server, and the lock freezes any other
            // instance for the duration of reconciliation + index build.
            migrationBuilder.Sql("""
                LOCK TABLE "TextbookJobs" IN ACCESS EXCLUSIVE MODE;

                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "ContentHash", "ConceptId"
                               ORDER BY (CASE WHEN "Status" = 'Completed' THEN 0 ELSE 1 END),
                                        "CreatedAt" DESC,
                                        "Id") AS rn
                    FROM "TextbookJobs"
                    WHERE "ContentHash" <> '' AND "Status" IN ('Pending', 'Processing', 'Completed')
                )
                DELETE FROM "TextbookChunks"
                WHERE "JobId" IN (SELECT "Id" FROM ranked WHERE rn > 1);

                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "ContentHash", "ConceptId"
                               ORDER BY (CASE WHEN "Status" = 'Completed' THEN 0 ELSE 1 END),
                                        "CreatedAt" DESC,
                                        "Id") AS rn
                    FROM "TextbookJobs"
                    WHERE "ContentHash" <> '' AND "Status" IN ('Pending', 'Processing', 'Completed')
                )
                UPDATE "TextbookJobs" t
                SET "Status" = 'Failed',
                    "Error" = 'Duplicate ingestion job reconciled before dedup enforcement. Re-upload the file to ingest it.'
                FROM ranked d
                WHERE t."Id" = d."Id" AND d.rn > 1;

                UPDATE "TextbookJobs"
                SET "Status" = 'Failed',
                    "Error" = 'Job created before content-hash tracking. Re-upload the file to ingest it.'
                WHERE "Status" = 'Pending' AND "ContentHash" = '';
                """);

            migrationBuilder.DropIndex(
                name: "IX_TextbookJobs_ContentHash_ConceptId",
                table: "TextbookJobs");

            migrationBuilder.CreateIndex(
                name: "IX_TextbookJobs_ContentHash_ConceptId",
                table: "TextbookJobs",
                columns: new[] { "ContentHash", "ConceptId" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Processing', 'Completed') AND \"ContentHash\" <> ''")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The legacy transition and duplicate reconciliation are
            // deliberately not reversible: transitioned rows cannot be told
            // apart from genuinely Failed jobs, and deleted chunks are gone.
            migrationBuilder.DropIndex(
                name: "IX_TextbookJobs_ContentHash_ConceptId",
                table: "TextbookJobs");

            migrationBuilder.CreateIndex(
                name: "IX_TextbookJobs_ContentHash_ConceptId",
                table: "TextbookJobs",
                columns: new[] { "ContentHash", "ConceptId" });
        }
    }
}
