using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "salary_back_payment",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    employee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_base = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: false),
                    payroll_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reference = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    months = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_salary_back_payment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "additional_pay",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    abbreviation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    source_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    salary_slip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_additional_pay", x => x.id);
                    table.ForeignKey(
                        name: "fk_additional_pay_salary_back_payment_source_id",
                        column: x => x.source_id,
                        principalSchema: "payroll",
                        principalTable: "salary_back_payment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_additional_pay_employee_id_payroll_date",
                schema: "payroll",
                table: "additional_pay",
                columns: new[] { "employee_id", "payroll_date" });

            migrationBuilder.CreateIndex(
                name: "ix_additional_pay_salary_slip_id",
                schema: "payroll",
                table: "additional_pay",
                column: "salary_slip_id");

            migrationBuilder.CreateIndex(
                name: "ix_additional_pay_source_id",
                schema: "payroll",
                table: "additional_pay",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_salary_back_payment_company_id_name",
                schema: "payroll",
                table: "salary_back_payment",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salary_back_payment_employee_id_effective_from",
                schema: "payroll",
                table: "salary_back_payment",
                columns: new[] { "employee_id", "effective_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "additional_pay",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "salary_back_payment",
                schema: "payroll");
        }
    }
}
