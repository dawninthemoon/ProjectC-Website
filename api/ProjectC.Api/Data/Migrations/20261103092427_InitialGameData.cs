using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectC.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialGameData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "game");

            migrationBuilder.CreateTable(
                name: "Players",
                schema: "game",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Decks",
                schema: "game",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CatalogVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Decks_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "game",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ranks",
                schema: "game",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mmr = table.Column<int>(type: "integer", nullable: false),
                    Tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Division = table.Column<int>(type: "integer", nullable: false),
                    Rp = table.Column<int>(type: "integer", nullable: false),
                    WinStreak = table.Column<int>(type: "integer", nullable: false),
                    PromotionStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ranks", x => x.PlayerId);
                    table.CheckConstraint("CK_Rank_Nonnegative", "\"Division\" >= 0 AND \"Rp\" >= 0 AND \"WinStreak\" >= 0");
                    table.ForeignKey(
                        name: "FK_Ranks_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "game",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeckCard",
                schema: "game",
                columns: table => new
                {
                    DeckId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeckCard", x => new { x.DeckId, x.CardId });
                    table.CheckConstraint("CK_DeckCard_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_DeckCard_Decks_DeckId",
                        column: x => x.DeckId,
                        principalSchema: "game",
                        principalTable: "Decks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromotionGame",
                schema: "game",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameNumber = table.Column<int>(type: "integer", nullable: false),
                    Won = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionGame", x => new { x.PlayerId, x.SeriesId, x.GameNumber });
                    table.CheckConstraint("CK_PromotionGame_Number", "\"GameNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_PromotionGame_Ranks_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "game",
                        principalTable: "Ranks",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchDeckCard",
                schema: "game",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchDeckCard", x => new { x.MatchId, x.PlayerId, x.CardId });
                    table.CheckConstraint("CK_MatchDeckCard_Quantity", "\"Quantity\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                schema: "game",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WinnerPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.MatchId);
                });

            migrationBuilder.CreateTable(
                name: "MatchParticipant",
                schema: "game",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeckId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeckName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CatalogVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchParticipant", x => new { x.MatchId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_MatchParticipant_Matches_MatchId",
                        column: x => x.MatchId,
                        principalSchema: "game",
                        principalTable: "Matches",
                        principalColumn: "MatchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchParticipant_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "game",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Decks_PlayerId",
                schema: "game",
                table: "Decks",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_MatchId_WinnerPlayerId",
                schema: "game",
                table: "Matches",
                columns: new[] { "MatchId", "WinnerPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipant_PlayerId",
                schema: "game",
                table: "MatchParticipant",
                column: "PlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_MatchDeckCard_MatchParticipant_MatchId_PlayerId",
                schema: "game",
                table: "MatchDeckCard",
                columns: new[] { "MatchId", "PlayerId" },
                principalSchema: "game",
                principalTable: "MatchParticipant",
                principalColumns: new[] { "MatchId", "PlayerId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_MatchParticipant_MatchId_WinnerPlayerId",
                schema: "game",
                table: "Matches",
                columns: new[] { "MatchId", "WinnerPlayerId" },
                principalSchema: "game",
                principalTable: "MatchParticipant",
                principalColumns: new[] { "MatchId", "PlayerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatchParticipant_Players_PlayerId",
                schema: "game",
                table: "MatchParticipant");

            migrationBuilder.DropForeignKey(
                name: "FK_Matches_MatchParticipant_MatchId_WinnerPlayerId",
                schema: "game",
                table: "Matches");

            migrationBuilder.DropTable(
                name: "DeckCard",
                schema: "game");

            migrationBuilder.DropTable(
                name: "MatchDeckCard",
                schema: "game");

            migrationBuilder.DropTable(
                name: "PromotionGame",
                schema: "game");

            migrationBuilder.DropTable(
                name: "Decks",
                schema: "game");

            migrationBuilder.DropTable(
                name: "Ranks",
                schema: "game");

            migrationBuilder.DropTable(
                name: "Players",
                schema: "game");

            migrationBuilder.DropTable(
                name: "MatchParticipant",
                schema: "game");

            migrationBuilder.DropTable(
                name: "Matches",
                schema: "game");
        }
    }
}
