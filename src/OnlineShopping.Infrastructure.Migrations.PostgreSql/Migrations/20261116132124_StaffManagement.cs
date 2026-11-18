using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShopping.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class StaffManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "suspended_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "staff_audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    target_identifier = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    details = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    roles = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    invited_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    expires_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accepted_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    accepted_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    revoked_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_invitations_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_audit_entries_created_utc",
                table: "staff_audit_entries",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_invited_by_user_id",
                table: "staff_invitations",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_normalized_email",
                table: "staff_invitations",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_phone_number",
                table: "staff_invitations",
                column: "phone_number");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_audit_entries");

            migrationBuilder.DropTable(
                name: "staff_invitations");

            migrationBuilder.DropColumn(
                name: "suspended_utc",
                table: "users");
        }
    }
}
