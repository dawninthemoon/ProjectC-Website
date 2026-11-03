using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectC.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BattlePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActivePromotionSeriesId",
                schema: "game",
                table: "Ranks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RankChangesJson",
                schema: "game",
                table: "Matches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionHash",
                schema: "game",
                table: "Matches",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BattleDeckSnapshots",
                schema: "game",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeckId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeckName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CatalogVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CardsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattleDeckSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BattleDeckSnapshots_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "game",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshSessions",
                schema: "game",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshSessions", x => x.TokenHash);
                    table.ForeignKey(
                        name: "FK_RefreshSessions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "game",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BattleDeckSnapshots_PlayerId",
                schema: "game",
                table: "BattleDeckSnapshots",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshSessions_ExpiresAt",
                schema: "game",
                table: "RefreshSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshSessions_FamilyId",
                schema: "game",
                table: "RefreshSessions",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshSessions_PlayerId",
                schema: "game",
                table: "RefreshSessions",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BattleDeckSnapshots",
                schema: "game");

            migrationBuilder.DropTable(
                name: "RefreshSessions",
                schema: "game");

            migrationBuilder.DropColumn(
                name: "ActivePromotionSeriesId",
                schema: "game",
                table: "Ranks");

            migrationBuilder.DropColumn(
                name: "RankChangesJson",
                schema: "game",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "SubmissionHash",
                schema: "game",
                table: "Matches");
        }
    }
}
