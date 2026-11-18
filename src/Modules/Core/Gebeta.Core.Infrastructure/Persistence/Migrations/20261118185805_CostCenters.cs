using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CostCenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cost_center",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    is_main = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_center", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cost_center_company_id",
                schema: "core",
                table: "cost_center",
                column: "company_id",
                unique: true,
                filter: "is_main");

            migrationBuilder.CreateIndex(
                name: "ix_cost_center_company_id_code",
                schema: "core",
                table: "cost_center",
                columns: new[] { "company_id", "code" },
                unique: true);

            // Every existing company gets its Main cost centre, as new companies do.
            migrationBuilder.Sql("""
                INSERT INTO core.cost_center (id, company_id, code, name, is_main, is_active, created_at, created_by)
                SELECT gen_random_uuid(), c.id, 'MAIN', 'Main', true, true, now(), 'system' FROM core.company c
                WHERE NOT EXISTS (SELECT 1 FROM core.cost_center x WHERE x.company_id = c.id AND x.is_main);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cost_center",
                schema: "core");
        }
    }
}
