using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HikvisionReplicator.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBackfillIntentVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "backfill_intents",
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
                table: "backfill_intents");
        }
    }
}
