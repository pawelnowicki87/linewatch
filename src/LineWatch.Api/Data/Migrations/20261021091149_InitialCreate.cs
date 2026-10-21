using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LineWatch.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alarms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MachineId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RaisedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClearedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alarms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "readings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MachineId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TemperatureC = table.Column<double>(type: "double precision", nullable: false),
                    SpeedRpm = table.Column<int>(type: "integer", nullable: false),
                    PartCount = table.Column<long>(type: "bigint", nullable: false),
                    FaultCode = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_readings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alarms_MachineId_Kind",
                table: "alarms",
                columns: new[] { "MachineId", "Kind" },
                unique: true,
                filter: "\"ClearedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_alarms_RaisedAt",
                table: "alarms",
                column: "RaisedAt");

            migrationBuilder.CreateIndex(
                name: "IX_readings_MachineId_Timestamp",
                table: "readings",
                columns: new[] { "MachineId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alarms");

            migrationBuilder.DropTable(
                name: "readings");
        }
    }
}
