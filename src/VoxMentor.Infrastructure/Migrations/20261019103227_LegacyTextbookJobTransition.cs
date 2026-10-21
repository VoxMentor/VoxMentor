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
            // end state with CompletedAt set (matching every other Failed
            // transition); Processing ones are left to their worker, and Completed
            // ones are left ingested (a retry of that content re-ingests once —
            // accepted one-off cost, see docs/API.md textbook section).
            // The earlier migration only had a NON-unique index, so concurrent
            // identical uploads could have written duplicate reusable rows;
            // reconcile them (and their chunks — TextbookChunks has no FK)
            // before the unique index is created. LOCK TABLE blocks reads and
            // writes on TextbookJobs: migrations run at startup before this
            // instance serves traffic or starts its Hangfire server, so no
            // live request path waits on it; other instances are frozen for
            // the duration of reconciliation + index build.
            migrationBuilder.Sql("""
                -- lock_timeout aborts this boot quickly if a conflicting lock
                -- is held, instead of blocking indefinitely. Lock order is
                -- consistent (TextbookJobs first; workers only take row locks
                -- they already hold), so there is no circular wait — only
                -- one-way blocking bounded by the timeout.
                SET LOCAL lock_timeout = '5s';

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
                    "Error" = 'Duplicate ingestion job reconciled before dedup enforcement. Re-upload the file to ingest it.',
                    "CompletedAt" = now()
                FROM ranked d
                WHERE t."Id" = d."Id" AND d.rn > 1;

                -- Keep rule (Completed first, then newest CreatedAt, then Id) is identical to the
                -- copy above: this CTE cannot span statements, so both must stay in sync. It drives
                -- both the chunk delete and the Failed transition. Precedence differs from
                -- FindReusableJobAsync (in-flight beats completed) deliberately: a completed corpus
                -- already serves retrieval, so it wins dedup.

                UPDATE "TextbookJobs"
                SET "Status" = 'Failed',
                    "Error" = 'Job created before content-hash tracking. Re-upload the file to ingest it.',
                    "CompletedAt" = now()
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
