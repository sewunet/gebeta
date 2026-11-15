using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payroll");

            migrationBuilder.CreateTable(
                name: "inbox_message",
                schema: "payroll",
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
                name: "naming_series_counter",
                schema: "payroll",
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
                schema: "payroll",
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

            migrationBuilder.CreateTable(
                name: "salary_slip",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    employee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    structure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    structure_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    @base = table.Column<decimal>(name: "base", type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    payment_days = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    total_working_days = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    gross_pay = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    taxable_earnings = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    total_deductions = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    net_pay = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    employer_contributions = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    tax_table = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_salary_slip", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "salary_structure",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    components = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_structure", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "salary_structure_assignment",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    structure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @base = table.Column<decimal>(name: "base", type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_structure_assignment", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "payroll",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_salary_slip_company_id_name",
                schema: "payroll",
                table: "salary_slip",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salary_slip_company_id_period_start",
                schema: "payroll",
                table: "salary_slip",
                columns: new[] { "company_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ix_salary_slip_employee_id_period_start",
                schema: "payroll",
                table: "salary_slip",
                columns: new[] { "employee_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ix_salary_structure_company_id_name",
                schema: "payroll",
                table: "salary_structure",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salary_structure_assignment_employee_id_from_date",
                schema: "payroll",
                table: "salary_structure_assignment",
                columns: new[] { "employee_id", "from_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_message",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "naming_series_counter",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "salary_slip",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "salary_structure",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "salary_structure_assignment",
                schema: "payroll");
        }
    }
}
