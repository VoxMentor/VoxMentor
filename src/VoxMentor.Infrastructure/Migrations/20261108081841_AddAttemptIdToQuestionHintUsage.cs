using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttemptIdToQuestionHintUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuestionHintUsages_UserId_QuestionId",
                table: "QuestionHintUsages");

            migrationBuilder.AddColumn<Guid>(
                name: "AttemptId",
                table: "QuestionHintUsages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_QuestionHintUsages_UserId_QuestionId_AttemptId",
                table: "QuestionHintUsages",
                columns: new[] { "UserId", "QuestionId", "AttemptId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuestionHintUsages_UserId_QuestionId_AttemptId",
                table: "QuestionHintUsages");

            migrationBuilder.DropColumn(
                name: "AttemptId",
                table: "QuestionHintUsages");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionHintUsages_UserId_QuestionId",
                table: "QuestionHintUsages",
                columns: new[] { "UserId", "QuestionId" },
                unique: true);
        }
    }
}
