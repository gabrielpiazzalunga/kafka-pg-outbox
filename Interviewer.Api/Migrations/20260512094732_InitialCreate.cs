using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interviewer.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InterviewTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfigJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InterviewSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OverallRating = table.Column<int>(type: "integer", nullable: true),
                    SummaryNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterviewSessions_InterviewTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "InterviewTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockConfigId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    NotesJson = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    CheckedItemsJson = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    RatingsJson = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockStates_InterviewSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "InterviewSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockStates_SessionId",
                table: "BlockStates",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewSessions_SessionCode",
                table: "InterviewSessions",
                column: "SessionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InterviewSessions_TemplateId",
                table: "InterviewSessions",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewTemplates_Code",
                table: "InterviewTemplates",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockStates");

            migrationBuilder.DropTable(
                name: "InterviewSessions");

            migrationBuilder.DropTable(
                name: "InterviewTemplates");
        }
    }
}
