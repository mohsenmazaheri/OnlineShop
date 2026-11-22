using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ProductComparison : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "better",
                table: "product_specs",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "compare_items",
                columns: table => new
                {
                    customer_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    added_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compare_items", x => new { x.customer_id, x.product_id });
                    table.ForeignKey(
                        name: "fk_compare_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_compare_items_users_customer_id",
                        column: x => x.customer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_compare_items_product_id",
                table: "compare_items",
                column: "product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compare_items");

            migrationBuilder.DropColumn(
                name: "better",
                table: "product_specs");
        }
    }
}
