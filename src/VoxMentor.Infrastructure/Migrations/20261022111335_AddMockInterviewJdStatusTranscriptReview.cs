using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMockInterviewJdStatusTranscriptReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "JdId",
                table: "MockInterviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewJson",
                table: "MockInterviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "MockInterviews",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "TranscriptJson",
                table: "MockInterviews",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockInterviews_JdId",
                table: "MockInterviews",
                column: "JdId");

            migrationBuilder.AddForeignKey(
                name: "FK_MockInterviews_JobDescriptions_JdId",
                table: "MockInterviews",
                column: "JdId",
                principalTable: "JobDescriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MockInterviews_JobDescriptions_JdId",
                table: "MockInterviews");

            migrationBuilder.DropIndex(
                name: "IX_MockInterviews_JdId",
                table: "MockInterviews");

            migrationBuilder.DropColumn(
                name: "JdId",
                table: "MockInterviews");

            migrationBuilder.DropColumn(
                name: "ReviewJson",
                table: "MockInterviews");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "MockInterviews");

            migrationBuilder.DropColumn(
                name: "TranscriptJson",
                table: "MockInterviews");
        }
    }
}
