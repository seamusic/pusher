using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessagePusher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    type = table.Column<string>(type: "varchar(32)", nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "varchar(32)", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    secret = table.Column<string>(type: "TEXT", nullable: false),
                    app_id = table.Column<string>(type: "TEXT", nullable: false),
                    account_id = table.Column<string>(type: "TEXT", nullable: false),
                    url = table.Column<string>(type: "TEXT", nullable: false),
                    other = table.Column<string>(type: "TEXT", nullable: false),
                    created_time = table.Column<long>(type: "bigint", nullable: false),
                    token = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    url = table.Column<string>(type: "TEXT", nullable: false),
                    channel = table.Column<string>(type: "TEXT", nullable: false),
                    timestamp = table.Column<long>(type: "bigint", nullable: false),
                    link = table.Column<string>(type: "TEXT", nullable: false),
                    to = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    render_mode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "options",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_options", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    username = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    password = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    role = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    token = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    github_id = table.Column<string>(type: "TEXT", nullable: false),
                    wechat_id = table.Column<string>(type: "TEXT", nullable: false),
                    channel = table.Column<string>(type: "TEXT", nullable: false),
                    send_email_to_others = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    save_message_to_database = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhooks",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "varchar(32)", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    link = table.Column<string>(type: "char(32)", nullable: false),
                    created_time = table.Column<long>(type: "bigint", nullable: false),
                    extract_rule = table.Column<string>(type: "TEXT", nullable: false),
                    construct_rule = table.Column<string>(type: "TEXT", nullable: false),
                    channel = table.Column<string>(type: "varchar(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhooks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_channels_secret",
                table: "channels",
                column: "secret");

            migrationBuilder.CreateIndex(
                name: "name_user_id",
                table: "channels",
                columns: new[] { "name", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_link",
                table: "messages",
                column: "link",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_status",
                table: "messages",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_messages_user_id",
                table: "messages",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_display_name",
                table: "users",
                column: "display_name");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "IX_users_github_id",
                table: "users",
                column: "github_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_wechat_id",
                table: "users",
                column: "wechat_id");

            migrationBuilder.CreateIndex(
                name: "IX_webhooks_link",
                table: "webhooks",
                column: "link",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhooks_name",
                table: "webhooks",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_webhooks_user_id",
                table: "webhooks",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "options");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "webhooks");
        }
    }
}
