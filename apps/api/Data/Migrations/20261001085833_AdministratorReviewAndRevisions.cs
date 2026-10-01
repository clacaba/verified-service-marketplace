using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdministratorReviewAndRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentApplicationId",
                table: "VerificationApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewLeaseExpiresAt",
                table: "VerificationApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewStartedAt",
                table: "VerificationApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStartedBy",
                table: "VerificationApplications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "VerificationApplications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsReviewer",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Message = table.Column<string>(type: "character varying(2200)", maxLength: 2200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_VerificationApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "VerificationApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformAccessAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAccessAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformAccessAudits_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerificationDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<string>(type: "text", nullable: false),
                    Decision = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationDecisions_AspNetUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationDecisions_VerificationApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "VerificationApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerificationChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Simulated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationChecks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationChecks_VerificationDecisions_DecisionId",
                        column: x => x.DecisionId,
                        principalTable: "VerificationDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationApplications_ParentApplicationId",
                table: "VerificationApplications",
                column: "ParentApplicationId",
                unique: true,
                filter: "\"ParentApplicationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationApplications_ReviewStartedBy",
                table: "VerificationApplications",
                column: "ReviewStartedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ApplicationId",
                table: "Notifications",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAccessAudits_UserId",
                table: "PlatformAccessAudits",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationChecks_DecisionId",
                table: "VerificationChecks",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationChecks_OrganizationId_ExpiresAt",
                table: "VerificationChecks",
                columns: new[] { "OrganizationId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationDecisions_ApplicationId",
                table: "VerificationDecisions",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationDecisions_ReviewerId",
                table: "VerificationDecisions",
                column: "ReviewerId");

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationApplications_AspNetUsers_ReviewStartedBy",
                table: "VerificationApplications",
                column: "ReviewStartedBy",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationApplications_VerificationApplications_ParentApp~",
                table: "VerificationApplications",
                column: "ParentApplicationId",
                principalTable: "VerificationApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VerificationApplications_AspNetUsers_ReviewStartedBy",
                table: "VerificationApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_VerificationApplications_VerificationApplications_ParentApp~",
                table: "VerificationApplications");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PlatformAccessAudits");

            migrationBuilder.DropTable(
                name: "VerificationChecks");

            migrationBuilder.DropTable(
                name: "VerificationDecisions");

            migrationBuilder.DropIndex(
                name: "IX_VerificationApplications_ParentApplicationId",
                table: "VerificationApplications");

            migrationBuilder.DropIndex(
                name: "IX_VerificationApplications_ReviewStartedBy",
                table: "VerificationApplications");

            migrationBuilder.DropColumn(
                name: "ParentApplicationId",
                table: "VerificationApplications");

            migrationBuilder.DropColumn(
                name: "ReviewLeaseExpiresAt",
                table: "VerificationApplications");

            migrationBuilder.DropColumn(
                name: "ReviewStartedAt",
                table: "VerificationApplications");

            migrationBuilder.DropColumn(
                name: "ReviewStartedBy",
                table: "VerificationApplications");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "VerificationApplications");

            migrationBuilder.DropColumn(
                name: "IsReviewer",
                table: "AspNetUsers");
        }
    }
}
