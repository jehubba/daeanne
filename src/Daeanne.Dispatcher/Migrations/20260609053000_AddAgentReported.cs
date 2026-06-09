using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daeanne.Dispatcher.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentReported : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AgentReported",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentReported",
                table: "Tasks");
        }
    }
}
