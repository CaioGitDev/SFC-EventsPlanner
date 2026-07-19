using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseXminConcurrencyForRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "FightResults",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Athletes",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "FightResults");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Athletes");
        }
    }
}
