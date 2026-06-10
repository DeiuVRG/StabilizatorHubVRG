using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StabilizatorHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultiUserDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceInvites",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MaxUses = table.Column<int>(type: "INTEGER", nullable: false),
                    UseCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceInvites_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceMemberships",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceMemberships_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceInvites_DeviceId_ExpiresAtUtc",
                table: "DeviceInvites",
                columns: new[] { "DeviceId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMemberships_DeviceId_UserId",
                table: "DeviceMemberships",
                columns: new[] { "DeviceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMemberships_UserId",
                table: "DeviceMemberships",
                column: "UserId");

            // Data move: every previously claimed device keeps its owner as an
            // Owner (role=1) membership before the legacy column disappears.
            migrationBuilder.Sql("""
                INSERT INTO DeviceMemberships (DeviceId, UserId, Role, JoinedAtUtc)
                SELECT Id, OwnerUserId, 1, COALESCE(ClaimedAtUtc, CreatedAtUtc)
                FROM Devices
                WHERE OwnerUserId IS NOT NULL
                """);

            migrationBuilder.DropIndex(
                name: "IX_Devices_OwnerUserId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Devices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceInvites");

            migrationBuilder.DropTable(
                name: "DeviceMemberships");

            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "Devices",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_OwnerUserId",
                table: "Devices",
                column: "OwnerUserId");
        }
    }
}
