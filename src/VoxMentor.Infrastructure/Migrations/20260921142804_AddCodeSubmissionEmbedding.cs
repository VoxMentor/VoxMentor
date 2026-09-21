using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeSubmissionEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.AddColumn<string>(
                name: "CodeEmbedding",
                table: "CodeSubmissions",
                type: "vector(768)",
                nullable: true);

            migrationBuilder.Sql("""
                CREATE INDEX "IX_CodeSubmissions_CodeEmbedding"
                ON "CodeSubmissions" USING hnsw ("CodeEmbedding" vector_cosine_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CodeSubmissions_CodeEmbedding",
                table: "CodeSubmissions");

            migrationBuilder.DropColumn(
                name: "CodeEmbedding",
                table: "CodeSubmissions");
        }
    }
}
