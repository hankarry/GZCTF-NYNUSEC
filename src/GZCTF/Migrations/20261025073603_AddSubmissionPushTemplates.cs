using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionPushTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_GamePushDeliveries",
                table: "GamePushDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_GamePushDeliveries_GameId_NoticeId",
                table: "GamePushDeliveries");

            migrationBuilder.AlterColumn<int>(
                name: "NoticeId",
                table: "GamePushDeliveries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "GamePushDeliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "SubmissionId",
                table: "GamePushDeliveries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "SubmissionResult",
                table: "GamePushDeliveries",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoticeTemplate",
                table: "GamePushConfigs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "【{game}】\n{content}\n{time} (UTC+8)");

            migrationBuilder.AddColumn<int>(
                name: "SubmissionEventMask",
                table: "GamePushConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionTemplate",
                table: "GamePushConfigs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "【{game}】[提交:{result}] 团队「{team}」成员「{member}」题目「{challenge}」{solve_rank}");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GamePushDeliveries",
                table: "GamePushDeliveries",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_GamePushDeliveries_GameId_Id",
                table: "GamePushDeliveries",
                columns: new[] { "GameId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GamePushDeliveries_NoticeId",
                table: "GamePushDeliveries",
                column: "NoticeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GamePushDeliveries_SubmissionId",
                table: "GamePushDeliveries",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GamePushDeliveries_Submissions_SubmissionId",
                table: "GamePushDeliveries",
                column: "SubmissionId",
                principalTable: "Submissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old schema only supports deliveries associated with public notices.
            migrationBuilder.Sql("DELETE FROM \"GamePushDeliveries\" WHERE \"NoticeId\" IS NULL");
            migrationBuilder.DropForeignKey(
                name: "FK_GamePushDeliveries_Submissions_SubmissionId",
                table: "GamePushDeliveries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GamePushDeliveries",
                table: "GamePushDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_GamePushDeliveries_GameId_Id",
                table: "GamePushDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_GamePushDeliveries_NoticeId",
                table: "GamePushDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_GamePushDeliveries_SubmissionId",
                table: "GamePushDeliveries");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "GamePushDeliveries");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "GamePushDeliveries");

            migrationBuilder.DropColumn(
                name: "SubmissionResult",
                table: "GamePushDeliveries");

            migrationBuilder.DropColumn(
                name: "NoticeTemplate",
                table: "GamePushConfigs");

            migrationBuilder.DropColumn(
                name: "SubmissionEventMask",
                table: "GamePushConfigs");

            migrationBuilder.DropColumn(
                name: "SubmissionTemplate",
                table: "GamePushConfigs");

            migrationBuilder.AlterColumn<int>(
                name: "NoticeId",
                table: "GamePushDeliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_GamePushDeliveries",
                table: "GamePushDeliveries",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePushDeliveries_GameId_NoticeId",
                table: "GamePushDeliveries",
                columns: new[] { "GameId", "NoticeId" });
        }
    }
}
