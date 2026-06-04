using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Starter.ApiService.Data.Migrations
{
    /// <inheritdoc />
    public partial class DevTodoCrud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "DevTodoItems" (
                    "Id" uuid NOT NULL,
                    "Title" character varying(140) NOT NULL,
                    "Notes" character varying(1000),
                    "Status" character varying(40) NOT NULL,
                    "DueDate" date,
                    "SortOrder" integer NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_DevTodoItems" PRIMARY KEY ("Id")
                );

                CREATE INDEX IF NOT EXISTS "IX_DevTodoItems_DueDate"
                    ON "DevTodoItems" ("DueDate");

                CREATE INDEX IF NOT EXISTS "IX_DevTodoItems_Status"
                    ON "DevTodoItems" ("Status");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DevTodoItems");
        }
    }
}
