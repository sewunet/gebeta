using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdditionalSalary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_additional_pay_salary_back_payment_source_id",
                schema: "payroll",
                table: "additional_pay");

            migrationBuilder.AddColumn<Guid>(
                name: "back_payment_id",
                schema: "payroll",
                table: "additional_pay",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "in_calculation",
                schema: "payroll",
                table: "additional_pay",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_taxable",
                schema: "payroll",
                table: "additional_pay",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "additional_salary",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_date = table.Column<DateOnly>(type: "date", nullable: false),
                    component = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    abbreviation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_taxable = table.Column<bool>(type: "boolean", nullable: false),
                    remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_additional_salary", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_additional_pay_back_payment_id",
                schema: "payroll",
                table: "additional_pay",
                column: "back_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_additional_salary_company_id_name",
                schema: "payroll",
                table: "additional_salary",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_additional_salary_company_id_payroll_date",
                schema: "payroll",
                table: "additional_salary",
                columns: new[] { "company_id", "payroll_date" });

            // Back payment entries keep their link in the new column; source_id stays as a plain reference for every source.
            migrationBuilder.Sql("UPDATE payroll.additional_pay SET back_payment_id = source_id WHERE source_type = 'Salary Back Payment';");

            migrationBuilder.AddForeignKey(
                name: "fk_additional_pay_salary_back_payment_back_payment_id",
                schema: "payroll",
                table: "additional_pay",
                column: "back_payment_id",
                principalSchema: "payroll",
                principalTable: "salary_back_payment",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_additional_pay_salary_back_payment_back_payment_id",
                schema: "payroll",
                table: "additional_pay");

            migrationBuilder.DropTable(
                name: "additional_salary",
                schema: "payroll");

            migrationBuilder.DropIndex(
                name: "ix_additional_pay_back_payment_id",
                schema: "payroll",
                table: "additional_pay");

            migrationBuilder.DropColumn(
                name: "back_payment_id",
                schema: "payroll",
                table: "additional_pay");

            migrationBuilder.DropColumn(
                name: "in_calculation",
                schema: "payroll",
                table: "additional_pay");

            migrationBuilder.DropColumn(
                name: "is_taxable",
                schema: "payroll",
                table: "additional_pay");

            migrationBuilder.AddForeignKey(
                name: "fk_additional_pay_salary_back_payment_source_id",
                schema: "payroll",
                table: "additional_pay",
                column: "source_id",
                principalSchema: "payroll",
                principalTable: "salary_back_payment",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
