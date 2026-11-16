using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pay_values",
                schema: "payroll",
                table: "salary_structure_assignment",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]"); // existing structures use no pay values

            migrationBuilder.AddColumn<string>(
                name: "variables",
                schema: "payroll",
                table: "salary_structure",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]"); // existing structures use no pay values
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pay_values",
                schema: "payroll",
                table: "salary_structure_assignment");

            migrationBuilder.DropColumn(
                name: "variables",
                schema: "payroll",
                table: "salary_structure");
        }
    }
}
