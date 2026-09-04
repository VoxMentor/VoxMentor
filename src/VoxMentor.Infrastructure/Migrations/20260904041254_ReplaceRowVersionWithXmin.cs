using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceRowVersionWithXmin : Migration
    {
        // NOTE: hand-edited. EF scaffolded a RenameColumn to "xmin", but xmin is a
        // PostgreSQL system column — it cannot be created or renamed to. Npgsql maps
        // the uint RowVersion directly to the built-in xmin, so Up only drops the
        // obsolete bytea column.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "StudentMasteries");
        }

        // Non-reversible: recreating RowVersion as a bytea column would restore a
        // concurrency token that PostgreSQL never updates (the original bug). Rollback
        // to AddStudentMasteryRowVersion requires manual SQL and is intentionally blocked.
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                $"Migration {nameof(ReplaceRowVersionWithXmin)} is not reversible. " +
                "Reverting would restore a bytea RowVersion token that PostgreSQL does not auto-update. " +
                "Roll back manually via SQL if strictly required.");
        }
    }
}
