using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OnlineShop.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ProductSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "search_documents",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    keywords = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    codes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_documents", x => new { x.product_id, x.locale });
                    table.ForeignKey(
                        name: "fk_search_documents_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    target = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enqueued_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "search_term_stats",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    term = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    searches = table.Column<int>(type: "integer", nullable: false),
                    zero_results = table.Column<int>(type: "integer", nullable: false),
                    last_searched_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_term_stats", x => new { x.locale, x.term });
                });

            migrationBuilder.CreateIndex(
                name: "ix_search_documents_locale",
                table: "search_documents",
                column: "locale");

            migrationBuilder.CreateIndex(
                name: "ix_search_term_stats_locale_searches",
                table: "search_term_stats",
                columns: new[] { "locale", "searches" });

            // PostgreSQL only: trigram matching, and the full-text vector computed from the document's parts with their
            // weights (A title, B keywords, C details and codes, D description) — kept by PostgreSQL, never by the app.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql("""
                ALTER TABLE search_documents ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A') ||
                    setweight(to_tsvector('simple', coalesce(keywords, '')), 'B') ||
                    setweight(to_tsvector('simple', coalesce(details, '') || ' ' || coalesce(codes, '')), 'C') ||
                    setweight(to_tsvector('simple', coalesce(description, '')), 'D')) STORED;
                """);
            migrationBuilder.Sql("CREATE INDEX ix_search_documents_search_vector ON search_documents USING GIN (search_vector);");
            migrationBuilder.Sql("CREATE INDEX ix_search_documents_title_trgm ON search_documents USING GIN (title gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_search_documents_title_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_search_documents_search_vector;");
            migrationBuilder.Sql("ALTER TABLE search_documents DROP COLUMN IF EXISTS search_vector;");

            migrationBuilder.DropTable(
                name: "search_documents");

            migrationBuilder.DropTable(
                name: "search_outbox");

            migrationBuilder.DropTable(
                name: "search_term_stats");
        }
    }
}
