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
            // Legacy transition (#104 CodeRabbit): rows predating content-hash
            // tracking keep ContentHash = '' forever (their staged files may be
            // gone — a backfill is impossible), so they can never match a
            // retry. Non-terminal ones stay Pending/Processing indefinitely
            // while a duplicate ingest runs; hand them an explicit end state:
            // Failed with a re-upload message. Completed legacy rows are left
            // alone (they really did finish); a retry of that content re-ingests
            // once — accepted one-off cost, see docs/API.md textbook section.
            migrationBuilder.Sql("""
                UPDATE "TextbookJobs"
                SET "Status" = 'Failed',
                    "Error" = 'Job created before content-hash tracking. Re-upload the file to ingest it.'
                WHERE "Status" IN ('Pending', 'Processing') AND "ContentHash" = '';
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
            // The legacy transition is deliberately not reversible: the
            // transitioned rows cannot be told apart from genuinely Failed jobs.
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
