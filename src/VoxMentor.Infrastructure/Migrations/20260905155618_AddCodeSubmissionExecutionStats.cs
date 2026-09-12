using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeSubmissionExecutionStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MemoryUsageKb",
                table: "CodeSubmissions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TestCasesPassed",
                table: "CodeSubmissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TestCasesTotal",
                table: "CodeSubmissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MemoryUsageKb",
                table: "CodeSubmissions");

            migrationBuilder.DropColumn(
                name: "TestCasesPassed",
                table: "CodeSubmissions");

            migrationBuilder.DropColumn(
                name: "TestCasesTotal",
                table: "CodeSubmissions");
        }
    }
}
