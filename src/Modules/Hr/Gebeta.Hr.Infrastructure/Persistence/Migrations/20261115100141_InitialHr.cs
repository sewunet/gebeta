using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Hr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialHr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hr");

            migrationBuilder.CreateTable(
                name: "attendance_record",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_message",
                schema: "hr",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_message", x => new { x.message_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "leave_application",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    days = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    approved_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    doc_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amended_from_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_application", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_ledger_entry",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    days = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fiscal_year = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_ledger_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "naming_series_counter",
                schema: "hr",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    current = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_naming_series_counter", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    routing_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    clr_type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_record_company_id_date",
                schema: "hr",
                table: "attendance_record",
                columns: new[] { "company_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_record_employee_id_date",
                schema: "hr",
                table: "attendance_record",
                columns: new[] { "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_application_company_id_name",
                schema: "hr",
                table: "leave_application",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_application_employee_id_from_date",
                schema: "hr",
                table: "leave_application",
                columns: new[] { "employee_id", "from_date" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_ledger_entry_employee_id_type",
                schema: "hr",
                table: "leave_ledger_entry",
                columns: new[] { "employee_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_ledger_entry_employee_id_type_fiscal_year",
                schema: "hr",
                table: "leave_ledger_entry",
                columns: new[] { "employee_id", "type", "fiscal_year" },
                unique: true,
                filter: "source = 'Allocation'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "hr",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_record",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "inbox_message",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "leave_application",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "leave_ledger_entry",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "naming_series_counter",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "hr");
        }
    }
}
