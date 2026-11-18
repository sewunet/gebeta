using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkUsersToEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "employee_id",
                schema: "core",
                table: "app_user",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_app_user_employee_id",
                schema: "core",
                table: "app_user",
                column: "employee_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_app_user_employee_id",
                schema: "core",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "employee_id",
                schema: "core",
                table: "app_user");
        }
    }
}
