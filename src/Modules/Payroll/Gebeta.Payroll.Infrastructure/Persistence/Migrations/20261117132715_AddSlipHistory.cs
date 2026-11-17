using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlipHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "additional",
                schema: "payroll",
                table: "salary_slip",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]"); // slips made before this kept no record of what was paid on top

            migrationBuilder.AddColumn<bool>(
                name: "is_history",
                schema: "payroll",
                table: "salary_slip",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "additional",
                schema: "payroll",
                table: "salary_slip");

            migrationBuilder.DropColumn(
                name: "is_history",
                schema: "payroll",
                table: "salary_slip");
        }
    }
}
