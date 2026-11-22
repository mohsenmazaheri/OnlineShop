using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class Stocktakes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stocktakes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    opened_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    closed_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stocktakes", x => x.id);
                    table.CheckConstraint("CK_Stocktakes_Status", "status IN ('Open', 'Posted', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_stocktakes_users_closed_by_user_id",
                        column: x => x.closed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stocktakes_users_opened_by_user_id",
                        column: x => x.opened_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stocktakes_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stocktake_lines",
                columns: table => new
                {
                    stocktake_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    snapshot_quantity = table.Column<int>(type: "integer", nullable: false),
                    counted_quantity = table.Column<int>(type: "integer", nullable: true),
                    counted_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expected_quantity = table.Column<int>(type: "integer", nullable: true),
                    posted_difference = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stocktake_lines", x => new { x.stocktake_id, x.item_id });
                    table.ForeignKey(
                        name: "fk_stocktake_lines_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stocktake_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stocktake_lines_stocktakes_stocktake_id",
                        column: x => x.stocktake_id,
                        principalTable: "stocktakes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_lines_product_id",
                table: "stocktake_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_lines_variant_id",
                table: "stocktake_lines",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stocktakes_closed_by_user_id",
                table: "stocktakes",
                column: "closed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stocktakes_number",
                table: "stocktakes",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stocktakes_opened_by_user_id",
                table: "stocktakes",
                column: "opened_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stocktakes_warehouse_id",
                table: "stocktakes",
                column: "warehouse_id",
                unique: true,
                filter: "status = 'Open' AND is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stocktake_lines");

            migrationBuilder.DropTable(
                name: "stocktakes");
        }
    }
}
