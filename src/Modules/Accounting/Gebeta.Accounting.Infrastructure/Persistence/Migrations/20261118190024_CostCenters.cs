using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CostCenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cost_center_id",
                schema: "accounting",
                table: "gl_entry",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_gl_entry_company_id_cost_center_id_posting_date",
                schema: "accounting",
                table: "gl_entry",
                columns: new[] { "company_id", "cost_center_id", "posting_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_gl_entry_company_id_cost_center_id_posting_date",
                schema: "accounting",
                table: "gl_entry");

            migrationBuilder.DropColumn(
                name: "cost_center_id",
                schema: "accounting",
                table: "gl_entry");
        }
    }
}
