using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddBlackoutEndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EndMessage",
                table: "GameBlackouts",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "本次禁赛时段已结束，请查看赛事页面的最新比赛状态。");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingEndAtUtc",
                table: "GameBlackouts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndMessage",
                table: "GameBlackouts");

            migrationBuilder.DropColumn(
                name: "PendingEndAtUtc",
                table: "GameBlackouts");
        }
    }
}
