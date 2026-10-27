using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HikvisionReplicator.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReplicationWorker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "replications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "replications",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_replications_device_in_progress",
                table: "replications",
                column: "DeviceId",
                unique: true,
                filter: "\"Status\" = 'InProgress'");

            migrationBuilder.CreateIndex(
                name: "IX_replications_device_pending",
                table: "replications",
                columns: new[] { "DeviceId", "Lane", "CreatedAt" },
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_replications_device_in_progress",
                table: "replications");

            migrationBuilder.DropIndex(
                name: "IX_replications_device_pending",
                table: "replications");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "replications");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "replications");
        }
    }
}
