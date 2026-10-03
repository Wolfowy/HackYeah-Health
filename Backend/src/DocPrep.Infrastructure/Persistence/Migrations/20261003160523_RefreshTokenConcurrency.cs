using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocPrep.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefreshTokenConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyVersion",
                schema: "docprep",
                table: "staff_refresh_tokens",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                schema: "docprep",
                table: "staff_refresh_tokens");
        }
    }
}
