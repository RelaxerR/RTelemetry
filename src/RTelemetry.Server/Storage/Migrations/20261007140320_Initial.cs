using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RTelemetry.Server.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Project = table.Column<string>(type: "text", nullable: false),
                    InstallId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AppVersion = table.Column<string>(type: "text", nullable: false),
                    ContentVersion = table.Column<string>(type: "text", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_InstallId",
                table: "Events",
                column: "InstallId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Project_Name_TimestampUtc",
                table: "Events",
                columns: new[] { "Project", "Name", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_Project_ReceivedAtUtc_AppVersion_ContentVersion",
                table: "Events",
                columns: new[] { "Project", "ReceivedAtUtc", "AppVersion", "ContentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_Project_SessionId_Sequence",
                table: "Events",
                columns: new[] { "Project", "SessionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_ReceivedAtUtc",
                table: "Events",
                column: "ReceivedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
