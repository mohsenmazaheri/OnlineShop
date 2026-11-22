using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LandingPagesAndSaleEnds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "sale_ends_utc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "landing_pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    intro = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    meta_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    meta_description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    query = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_landing_pages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "landing_page_translations",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    landing_page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    meta_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    meta_description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    tags = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_landing_page_translations", x => new { x.landing_page_id, x.locale });
                    table.ForeignKey(
                        name: "fk_landing_page_translations_landing_pages_landing_page_id",
                        column: x => x.landing_page_id,
                        principalTable: "landing_pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_sale_ends_utc",
                table: "products",
                column: "sale_ends_utc");

            migrationBuilder.CreateIndex(
                name: "ix_landing_pages_slug",
                table: "landing_pages",
                column: "slug",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "landing_page_translations");

            migrationBuilder.DropTable(
                name: "landing_pages");

            migrationBuilder.DropIndex(
                name: "ix_products_sale_ends_utc",
                table: "products");

            migrationBuilder.DropColumn(
                name: "sale_ends_utc",
                table: "products");
        }
    }
}
