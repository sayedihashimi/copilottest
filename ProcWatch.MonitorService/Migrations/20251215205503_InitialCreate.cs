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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TargetPid = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetProcessName = table.Column<string>(type: "TEXT", nullable: false),
                    MonitorChildren = table.Column<bool>(type: "INTEGER", nullable: false),
                    DatabasePath = table.Column<string>(type: "TEXT", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Pid = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", nullable: false),
                    CommandLine = table.Column<string>(type: "TEXT", nullable: true),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExitTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ParentPid = table.Column<int>(type: "INTEGER", nullable: true),
                    IsTarget = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessInstances_MonitoredSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MonitoredSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    Operation = table.Column<string>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: true),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    BytesTransferred = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventRecords_MonitoredSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MonitoredSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventRecords_ProcessInstances_ProcessInstanceId",
                        column: x => x.ProcessInstanceId,
                        principalTable: "ProcessInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StatsSamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CpuPercent = table.Column<double>(type: "REAL", nullable: false),
                    MemoryBytes = table.Column<long>(type: "INTEGER", nullable: false),
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
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatsSamples_ProcessInstances_ProcessInstanceId",
                        column: x => x.ProcessInstanceId,
                        principalTable: "ProcessInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventRecords_EventType",
                table: "EventRecords",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_EventRecords_ProcessInstanceId",
                table: "EventRecords",
                column: "ProcessInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_EventRecords_SessionId",
                table: "EventRecords",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EventRecords_Timestamp",
                table: "EventRecords",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredSessions_StartTime",
                table: "MonitoredSessions",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredSessions_TargetPid",
                table: "MonitoredSessions",
                column: "TargetPid");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessInstances_Pid",
                table: "ProcessInstances",
                column: "Pid");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessInstances_SessionId",
                table: "ProcessInstances",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessInstances_SessionId_Pid",
                table: "ProcessInstances",
                columns: new[] { "SessionId", "Pid" });

            migrationBuilder.CreateIndex(
                name: "IX_StatsSamples_ProcessInstanceId",
                table: "StatsSamples",
                column: "ProcessInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_StatsSamples_SessionId",
                table: "StatsSamples",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StatsSamples_Timestamp",
                table: "StatsSamples",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventRecords");

            migrationBuilder.DropTable(
                name: "StatsSamples");

            migrationBuilder.DropTable(
                name: "ProcessInstances");

            migrationBuilder.DropTable(
                name: "MonitoredSessions");
        }
    }
}
