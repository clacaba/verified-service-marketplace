using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Marketplace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProviderProfileDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ProviderType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Biography = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ServiceArea = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderProfiles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCategories",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCategories", x => new { x.ProfileId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_ProviderCategories_ProviderProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "ProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProviderCategories_ServiceCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ServiceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ServiceCategories",
                columns: new[] { "Id", "Active", "Name" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), true, "Cleaning" },
                    { new Guid("10000000-0000-0000-0000-000000000002"), true, "Plumbing" },
                    { new Guid("10000000-0000-0000-0000-000000000003"), true, "Electrical" },
                    { new Guid("10000000-0000-0000-0000-000000000004"), true, "Appliance repair" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCategories_CategoryId",
                table: "ProviderCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderProfiles_OrganizationId",
                table: "ProviderProfiles",
                column: "OrganizationId",
                unique: true);
            migrationBuilder.Sql("""
                INSERT INTO "ProviderProfiles" ("Id", "OrganizationId", "DisplayName", "ProviderType", "Biography", "ServiceArea", "Version", "UpdatedAt")
                SELECT "Id", "Id", "Name", "ProviderType", '', '', "Id", CURRENT_TIMESTAMP FROM "Organizations";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderCategories");

            migrationBuilder.DropTable(
                name: "ProviderProfiles");

            migrationBuilder.DropTable(
                name: "ServiceCategories");
        }
    }
}
