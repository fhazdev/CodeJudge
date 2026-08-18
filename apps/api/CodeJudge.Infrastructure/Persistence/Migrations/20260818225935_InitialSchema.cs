using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeJudge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    statement_md = table.Column<string>(type: "text", nullable: false),
                    constraints_md = table.Column<string>(type: "text", nullable: true),
                    starter_code = table.Column<string>(type: "text", nullable: false),
                    harness_code = table.Column<string>(type: "text", nullable: false),
                    time_limit_ms = table.Column<int>(type: "integer", nullable: false),
                    memory_limit_kb = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_problems", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entra_object_id = table.Column<string>(type: "text", nullable: false),
                    entra_tenant_id = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    input = table.Column<string>(type: "text", nullable: false),
                    expected_output = table.Column<string>(type: "text", nullable: false),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_cases", x => x.id);
                    table.ForeignKey(
                        name: "FK_test_cases_problems_problem_id",
                        column: x => x.problem_id,
                        principalTable: "problems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    runtime_ms = table.Column<int>(type: "integer", nullable: true),
                    memory_kb = table.Column<int>(type: "integer", nullable: true),
                    failed_case_ordinal = table.Column<int>(type: "integer", nullable: true),
                    stderr_excerpt = table.Column<string>(type: "text", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_submissions_problems_problem_id",
                        column: x => x.problem_id,
                        principalTable: "problems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_problems_slug",
                table: "problems",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_problem_id",
                table: "submissions",
                column: "problem_id");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_user_id_problem_id_created_at",
                table: "submissions",
                columns: new[] { "user_id", "problem_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_test_cases_problem_id_ordinal",
                table: "test_cases",
                columns: new[] { "problem_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_entra_tenant_id_entra_object_id",
                table: "users",
                columns: new[] { "entra_tenant_id", "entra_object_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "test_cases");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "problems");
        }
    }
}
