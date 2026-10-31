using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddGameTimeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameBlackouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Daily = table.Column<bool>(type: "boolean", nullable: false),
                    StartAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartMinute = table.Column<int>(type: "integer", nullable: false),
                    EndMinute = table.Column<int>(type: "integer", nullable: false),
                    Notify = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    LastStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameBlackouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameBlackouts_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameScheduledTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    AtGameStart = table.Column<bool>(type: "boolean", nullable: false),
                    ExecuteAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ChallengeIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    EnableChallenges = table.Column<bool>(type: "boolean", nullable: false),
                    Notify = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ExecutedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameScheduledTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameScheduledTasks_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameBlackouts_GameId_Enabled",
                table: "GameBlackouts",
                columns: new[] { "GameId", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_GameScheduledTasks_Enabled_ExecutedAtUtc",
                table: "GameScheduledTasks",
                columns: new[] { "Enabled", "ExecutedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GameScheduledTasks_GameId",
                table: "GameScheduledTasks",
                column: "GameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameBlackouts");

            migrationBuilder.DropTable(
                name: "GameScheduledTasks");
        }
    }
}
