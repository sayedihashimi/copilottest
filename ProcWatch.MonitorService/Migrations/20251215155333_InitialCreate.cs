using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcWatch.MonitorService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonitoredSessions",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TargetPid = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IncludeChildren = table.Column<bool>(type: "INTEGER", nullable: false),
                    ArgsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredSessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "EventRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Pid = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Op = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Endpoints = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    JsonPayload = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventRecords_MonitoredSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MonitoredSessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Pid = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentPid = table.Column<int>(type: "INTEGER", nullable: true),
                    ProcessName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CommandLine = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessInstances_MonitoredSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MonitoredSessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StatsSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Pid = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CpuPercent = table.Column<double>(type: "REAL", nullable: false),
                    WorkingSetBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    PrivateBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    HandleCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ThreadCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatsSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatsSamples_MonitoredSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MonitoredSessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventRecords_Path",
                table: "EventRecords",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_EventRecords_SessionId_Timestamp",
                table: "EventRecords",
                columns: new[] { "SessionId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_EventRecords_SessionId_Type",
                table: "EventRecords",
                columns: new[] { "SessionId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessInstances_SessionId_Pid",
                table: "ProcessInstances",
                columns: new[] { "SessionId", "Pid" });

            migrationBuilder.CreateIndex(
                name: "IX_StatsSamples_SessionId_Pid",
                table: "StatsSamples",
                columns: new[] { "SessionId", "Pid" });

            migrationBuilder.CreateIndex(
                name: "IX_StatsSamples_SessionId_Timestamp",
                table: "StatsSamples",
                columns: new[] { "SessionId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventRecords");

            migrationBuilder.DropTable(
                name: "ProcessInstances");

            migrationBuilder.DropTable(
                name: "StatsSamples");

            migrationBuilder.DropTable(
                name: "MonitoredSessions");
        }
    }
}
