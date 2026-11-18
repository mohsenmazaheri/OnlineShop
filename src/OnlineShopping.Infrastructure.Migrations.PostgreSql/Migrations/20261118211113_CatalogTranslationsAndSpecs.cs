using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShopping.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class CatalogTranslationsAndSpecs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "public_number",
                table: "products",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Products that already exist get distinct numbers (1001, 1002, …) before the unique index below.
            migrationBuilder.Sql(
                """
                UPDATE products p SET public_number = n.number
                FROM (SELECT id, 1000 + ROW_NUMBER() OVER (ORDER BY created_utc, id) AS number FROM products) n
                WHERE p.id = n.id;
                """);

            migrationBuilder.AddColumn<string>(
                name: "slug",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                table: "categories",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "category_translations",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category_translations", x => new { x.category_id, x.locale });
                    table.ForeignKey(
                        name: "fk_category_translations_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_group_translations",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    product_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_group_translations", x => new { x.product_group_id, x.locale });
                    table.ForeignKey(
                        name: "fk_product_group_translations_product_groups_product_group_id",
                        column: x => x.product_group_id,
                        principalTable: "product_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_specs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    group_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_filterable = table.Column<bool>(type: "boolean", nullable: false),
                    is_comparable = table.Column<bool>(type: "boolean", nullable: false),
                    show_in_summary = table.Column<bool>(type: "boolean", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_specs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_translations",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_translations", x => new { x.product_id, x.locale });
                    table.ForeignKey(
                        name: "fk_product_translations_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sub_category_translations",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    sub_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sub_category_translations", x => new { x.sub_category_id, x.locale });
                    table.ForeignKey(
                        name: "fk_sub_category_translations_sub_categories_sub_category_id",
                        column: x => x.sub_category_id,
                        principalTable: "sub_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_spec_translations",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    spec_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    group_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_spec_translations", x => new { x.spec_id, x.locale });
                    table.ForeignKey(
                        name: "fk_product_spec_translations_product_specs_spec_id",
                        column: x => x.spec_id,
                        principalTable: "product_specs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "spec_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spec_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_spec_options_product_specs_spec_id",
                        column: x => x.spec_id,
                        principalTable: "product_specs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_spec_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    boolean_value = table.Column<bool>(type: "boolean", nullable: true),
                    text_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_spec_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_spec_values_product_specs_spec_id",
                        column: x => x.spec_id,
                        principalTable: "product_specs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_spec_values_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_spec_values_spec_options_option_id",
                        column: x => x.option_id,
                        principalTable: "spec_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "spec_option_translations",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spec_option_translations", x => new { x.option_id, x.locale });
                    table.ForeignKey(
                        name: "fk_spec_option_translations_spec_options_option_id",
                        column: x => x.option_id,
                        principalTable: "spec_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_public_number",
                table: "products",
                column: "public_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_spec_values_option_id",
                table: "product_spec_values",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_spec_values_product_id_spec_id",
                table: "product_spec_values",
                columns: new[] { "product_id", "spec_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_product_spec_values_spec_id_number_value",
                table: "product_spec_values",
                columns: new[] { "spec_id", "number_value" });

            migrationBuilder.CreateIndex(
                name: "ix_product_spec_values_spec_id_option_id",
                table: "product_spec_values",
                columns: new[] { "spec_id", "option_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_specs_code",
                table: "product_specs",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_spec_options_spec_id_code",
                table: "spec_options",
                columns: new[] { "spec_id", "code" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "category_translations");

            migrationBuilder.DropTable(
                name: "product_group_translations");

            migrationBuilder.DropTable(
                name: "product_spec_translations");

            migrationBuilder.DropTable(
                name: "product_spec_values");

            migrationBuilder.DropTable(
                name: "product_translations");

            migrationBuilder.DropTable(
                name: "spec_option_translations");

            migrationBuilder.DropTable(
                name: "sub_category_translations");

            migrationBuilder.DropTable(
                name: "spec_options");

            migrationBuilder.DropTable(
                name: "product_specs");

            migrationBuilder.DropIndex(
                name: "ix_products_public_number",
                table: "products");

            migrationBuilder.DropColumn(
                name: "public_number",
                table: "products");

            migrationBuilder.DropColumn(
                name: "slug",
                table: "products");

            migrationBuilder.DropColumn(
                name: "image_url",
                table: "categories");
        }
    }
}
