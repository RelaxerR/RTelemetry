using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RTelemetry.Server.Storage.Migrations
{
    /// <inheritdoc />
    public partial class DeletedInstalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeletedInstalls",
                columns: table => new
                {
                    InstallId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedInstalls", x => x.InstallId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeletedInstalls");
        }
    }
}
