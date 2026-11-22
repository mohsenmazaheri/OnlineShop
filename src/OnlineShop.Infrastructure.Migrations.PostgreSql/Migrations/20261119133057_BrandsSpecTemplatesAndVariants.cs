using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class BrandsSpecTemplatesAndVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "brand_id",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "brands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    logo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category_template_specs",
                columns: table => new
                {
                    spec_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category_template_specs", x => new { x.category_id, x.spec_id });
                    table.ForeignKey(
                        name: "fk_category_template_specs_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_category_template_specs_product_specs_spec_id",
                        column: x => x.spec_id,
                        principalTable: "product_specs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_variants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    compare_at_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    stock = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variants", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_variants_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sub_category_template_specs",
                columns: table => new
                {
                    spec_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sub_category_template_specs", x => new { x.sub_category_id, x.spec_id });
                    table.ForeignKey(
                        name: "fk_sub_category_template_specs_product_specs_spec_id",
                        column: x => x.spec_id,
                        principalTable: "product_specs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sub_category_template_specs_sub_categories_sub_category_id",
                        column: x => x.sub_category_id,
                        principalTable: "sub_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "brand_translations",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brand_translations", x => new { x.brand_id, x.locale });
                    table.ForeignKey(
                        name: "fk_brand_translations_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_variant_options",
                columns: table => new
                {
                    spec_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variant_options", x => new { x.variant_id, x.spec_id });
                    table.ForeignKey(
                        name: "fk_product_variant_options_product_specs_spec_id",
                        column: x => x.spec_id,
                        principalTable: "product_specs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_variant_options_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_variant_options_spec_options_option_id",
                        column: x => x.option_id,
                        principalTable: "spec_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_brand_id",
                table: "products",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_brands_slug",
                table: "brands",
                column: "slug",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_category_template_specs_spec_id",
                table: "category_template_specs",
                column: "spec_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_options_option_id",
                table: "product_variant_options",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_options_spec_id",
                table: "product_variant_options",
                column: "spec_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_product_id",
                table: "product_variants",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_sku",
                table: "product_variants",
                column: "sku",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_sub_category_template_specs_spec_id",
                table: "sub_category_template_specs",
                column: "spec_id");

            migrationBuilder.AddForeignKey(
                name: "fk_products_brands_brand_id",
                table: "products",
                column: "brand_id",
                principalTable: "brands",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_products_brands_brand_id",
                table: "products");

            migrationBuilder.DropTable(
                name: "brand_translations");

            migrationBuilder.DropTable(
                name: "category_template_specs");

            migrationBuilder.DropTable(
                name: "product_variant_options");

            migrationBuilder.DropTable(
                name: "sub_category_template_specs");

            migrationBuilder.DropTable(
                name: "brands");

            migrationBuilder.DropTable(
                name: "product_variants");

            migrationBuilder.DropIndex(
                name: "ix_products_brand_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "brand_id",
                table: "products");
        }
    }
}
