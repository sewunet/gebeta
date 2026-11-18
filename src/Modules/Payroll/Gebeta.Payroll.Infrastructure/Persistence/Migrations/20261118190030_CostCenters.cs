using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CostCenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cost_center_id",
                schema: "payroll",
                table: "employee_payroll_profile",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cost_center_id",
                schema: "payroll",
                table: "employee_payroll_profile");
        }
    }
}
