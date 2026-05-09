using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliverySimulator.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SimulationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CityId = table.Column<int>(type: "INTEGER", nullable: false),
                    RunAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Total = table.Column<int>(type: "INTEGER", nullable: false),
                    Delivered = table.Column<int>(type: "INTEGER", nullable: false),
                    Late = table.Column<int>(type: "INTEGER", nullable: false),
                    Unassigned = table.Column<int>(type: "INTEGER", nullable: false),
                    ElapsedSeconds = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SimRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Customer = table.Column<string>(type: "TEXT", nullable: false),
                    CourierId = table.Column<int>(type: "INTEGER", nullable: true),
                    CourierName = table.Column<string>(type: "TEXT", nullable: true),
                    WasDelivered = table.Column<bool>(type: "INTEGER", nullable: false),
                    WasLate = table.Column<bool>(type: "INTEGER", nullable: false),
                    IdealMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    ActualMinutes = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryLogs_SimulationRuns_SimRunId",
                        column: x => x.SimRunId,
                        principalTable: "SimulationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryLogs_SimRunId",
                table: "DeliveryLogs",
                column: "SimRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryLogs");

            migrationBuilder.DropTable(
                name: "SimulationRuns");
        }
    }
}
