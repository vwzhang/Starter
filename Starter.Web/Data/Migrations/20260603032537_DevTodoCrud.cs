using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Starter.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DevTodoCrud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DevTodoItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevTodoItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DevTodoItems_DueDate",
                table: "DevTodoItems",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_DevTodoItems_Status",
                table: "DevTodoItems",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DevTodoItems");
        }
    }
}
