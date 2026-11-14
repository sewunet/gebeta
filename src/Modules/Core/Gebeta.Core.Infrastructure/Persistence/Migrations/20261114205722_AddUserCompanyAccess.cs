using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCompanyAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "company_ids",
                schema: "core",
                table: "app_user",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]"); // existing users start with no company access
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "company_ids",
                schema: "core",
                table: "app_user");
        }
    }
}
