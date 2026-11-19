using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShopping.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ProductSeoFieldsSynonymsAndRedirects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "meta_description",
                table: "sub_category_translations",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_title",
                table: "sub_category_translations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tags",
                table: "sub_category_translations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gtin",
                table: "products",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_description",
                table: "products",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_title",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mpn",
                table: "products",
                type: "character varying(70)",
                maxLength: 70,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tags",
                table: "products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "weight_grams",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gtin",
                table: "product_variants",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_description",
                table: "product_translations",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_title",
                table: "product_translations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tags",
                table: "product_translations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_description",
                table: "product_group_translations",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_title",
                table: "product_group_translations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tags",
                table: "product_group_translations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_description",
                table: "category_translations",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_title",
                table: "category_translations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tags",
                table: "category_translations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_description",
                table: "brand_translations",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meta_title",
                table: "brand_translations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tags",
                table: "brand_translations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "redirects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    to_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    hit_count = table.Column<long>(type: "bigint", nullable: false),
                    last_hit_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_redirects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "search_synonyms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    terms = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_synonyms", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_gtin",
                table: "products",
                column: "gtin");

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_gtin",
                table: "product_variants",
                column: "gtin");

            migrationBuilder.CreateIndex(
                name: "ix_redirects_from_path",
                table: "redirects",
                column: "from_path",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_search_synonyms_locale",
                table: "search_synonyms",
                column: "locale");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "redirects");

            migrationBuilder.DropTable(
                name: "search_synonyms");

            migrationBuilder.DropIndex(
                name: "ix_products_gtin",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_product_variants_gtin",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "meta_description",
                table: "sub_category_translations");

            migrationBuilder.DropColumn(
                name: "meta_title",
                table: "sub_category_translations");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "sub_category_translations");

            migrationBuilder.DropColumn(
                name: "gtin",
                table: "products");

            migrationBuilder.DropColumn(
                name: "meta_description",
                table: "products");

            migrationBuilder.DropColumn(
                name: "meta_title",
                table: "products");

            migrationBuilder.DropColumn(
                name: "mpn",
                table: "products");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "products");

            migrationBuilder.DropColumn(
                name: "weight_grams",
                table: "products");

            migrationBuilder.DropColumn(
                name: "gtin",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "meta_description",
                table: "product_translations");

            migrationBuilder.DropColumn(
                name: "meta_title",
                table: "product_translations");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "product_translations");

            migrationBuilder.DropColumn(
                name: "meta_description",
                table: "product_group_translations");

            migrationBuilder.DropColumn(
                name: "meta_title",
                table: "product_group_translations");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "product_group_translations");

            migrationBuilder.DropColumn(
                name: "meta_description",
                table: "category_translations");

            migrationBuilder.DropColumn(
                name: "meta_title",
                table: "category_translations");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "category_translations");

            migrationBuilder.DropColumn(
                name: "meta_description",
                table: "brand_translations");

            migrationBuilder.DropColumn(
                name: "meta_title",
                table: "brand_translations");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "brand_translations");
        }
    }
}
