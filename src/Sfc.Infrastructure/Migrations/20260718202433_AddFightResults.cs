using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFightResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResultDraws",
                table: "Athletes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ResultKos",
                table: "Athletes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ResultLosses",
                table: "Athletes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ResultWins",
                table: "Athletes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FightResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FightId = table.Column<Guid>(type: "uuid", nullable: false),
                    WinnerAthleteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: true),
                    Time = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FightResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FightResults_Athletes_WinnerAthleteId",
                        column: x => x.WinnerAthleteId,
                        principalTable: "Athletes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FightResults_Fights_FightId",
                        column: x => x.FightId,
                        principalTable: "Fights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FightResults_FightId",
                table: "FightResults",
                column: "FightId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FightResults_WinnerAthleteId",
                table: "FightResults",
                column: "WinnerAthleteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FightResults");

            migrationBuilder.DropColumn(
                name: "ResultDraws",
                table: "Athletes");

            migrationBuilder.DropColumn(
                name: "ResultKos",
                table: "Athletes");

            migrationBuilder.DropColumn(
                name: "ResultLosses",
                table: "Athletes");

            migrationBuilder.DropColumn(
                name: "ResultWins",
                table: "Athletes");
        }
    }
}
