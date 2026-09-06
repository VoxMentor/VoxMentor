using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionHiddenTestCaseCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HiddenTestCaseCount",
                table: "Questions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Questions_HiddenTestCaseCount_NonNegative",
                table: "Questions",
                sql: "\"HiddenTestCaseCount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Questions_HiddenTestCaseCount_NonNegative",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "HiddenTestCaseCount",
                table: "Questions");
        }
    }
}
