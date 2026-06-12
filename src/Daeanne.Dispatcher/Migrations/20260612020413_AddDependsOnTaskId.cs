using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daeanne.Dispatcher.Migrations
{
    /// <inheritdoc />
    public partial class AddDependsOnTaskId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DependsOnTaskId",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_DependsOnTaskId",
                table: "Tasks",
                column: "DependsOnTaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_DependsOnTaskId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "DependsOnTaskId",
                table: "Tasks");
        }
    }
}
