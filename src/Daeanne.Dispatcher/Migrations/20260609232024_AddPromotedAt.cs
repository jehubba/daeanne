using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daeanne.Dispatcher.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only adds PromotedAt — all other columns (CallbackAcknowledgedAt,
            // CallbackPostedAt, IsScheduled, ParentTaskId, etc.) were applied
            // outside EF's tracking and already exist in the DB.
            migrationBuilder.AddColumn<DateTime>(
                name: "PromotedAt",
                table: "Tasks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromotedAt",
                table: "Tasks");
        }
    }
}
