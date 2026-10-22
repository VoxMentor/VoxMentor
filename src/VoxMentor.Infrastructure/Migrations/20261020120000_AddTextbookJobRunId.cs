using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxMentor.Infrastructure.Persistence;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Hand-written: EF Core only registers Migration-derived types that
    // carry BOTH [DbContext] and [Migration] — without these MigrateAsync
    // silently skips the migration and never creates the column
    // (#104 CodeRabbit round 5).
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261020120000_AddTextbookJobRunId")]
    public partial class AddTextbookJobRunId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RunId",
                table: "TextbookJobs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RunId",
                table: "TextbookJobs");
        }
    }
}
