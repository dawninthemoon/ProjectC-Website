-- Existing EF game tables require reviewed baselining; see docs/stack-migration.md.
BEGIN;
-- CreateSchema
CREATE SCHEMA IF NOT EXISTS "game";

-- CreateTable
CREATE TABLE "game"."Players" (
    "Id" UUID NOT NULL,
    "DisplayName" VARCHAR(32) NOT NULL,
    "CreatedAt" TIMESTAMPTZ(6) NOT NULL,

    CONSTRAINT "PK_Players" PRIMARY KEY ("Id")
);

-- CreateTable
CREATE TABLE "game"."RefreshSessions" (
    "TokenHash" VARCHAR(64) NOT NULL,
    "PlayerId" UUID NOT NULL,
    "FamilyId" UUID NOT NULL,
    "ExpiresAt" TIMESTAMPTZ(6) NOT NULL,
    "RevokedAt" TIMESTAMPTZ(6),

    CONSTRAINT "PK_RefreshSessions" PRIMARY KEY ("TokenHash")
);

-- CreateTable
CREATE TABLE "game"."Decks" (
    "Id" UUID NOT NULL,
    "PlayerId" UUID NOT NULL,
    "Name" VARCHAR(64) NOT NULL,
    "CatalogVersion" VARCHAR(128) NOT NULL,
    "CreatedAt" TIMESTAMPTZ(6) NOT NULL,
    "UpdatedAt" TIMESTAMPTZ(6) NOT NULL,

    CONSTRAINT "PK_Decks" PRIMARY KEY ("Id")
);

-- CreateTable
CREATE TABLE "game"."DeckCard" (
    "DeckId" UUID NOT NULL,
    "CardId" INTEGER NOT NULL,
    "Quantity" INTEGER NOT NULL,

    CONSTRAINT "PK_DeckCard" PRIMARY KEY ("DeckId","CardId")
);

-- CreateTable
CREATE TABLE "game"."Ranks" (
    "PlayerId" UUID NOT NULL,
    "Mmr" INTEGER NOT NULL,
    "Tier" VARCHAR(32) NOT NULL,
    "Division" INTEGER NOT NULL,
    "Rp" INTEGER NOT NULL,
    "WinStreak" INTEGER NOT NULL,
    "PromotionStatus" VARCHAR(32) NOT NULL,
    "ActivePromotionSeriesId" UUID,

    CONSTRAINT "PK_Ranks" PRIMARY KEY ("PlayerId")
);

-- CreateTable
CREATE TABLE "game"."PromotionGame" (
    "PlayerId" UUID NOT NULL,
    "SeriesId" UUID NOT NULL,
    "GameNumber" INTEGER NOT NULL,
    "Won" BOOLEAN NOT NULL,

    CONSTRAINT "PK_PromotionGame" PRIMARY KEY ("PlayerId","SeriesId","GameNumber")
);

-- CreateTable
CREATE TABLE "game"."BattleDeckSnapshots" (
    "Id" UUID NOT NULL,
    "PlayerId" UUID NOT NULL,
    "DeckId" UUID NOT NULL,
    "DeckName" VARCHAR(64) NOT NULL,
    "CatalogVersion" VARCHAR(128) NOT NULL,
    "CardsJson" TEXT NOT NULL,
    "CreatedAt" TIMESTAMPTZ(6) NOT NULL,

    CONSTRAINT "PK_BattleDeckSnapshots" PRIMARY KEY ("Id")
);

-- CreateTable
CREATE TABLE "game"."Matches" (
    "MatchId" UUID NOT NULL,
    "WinnerPlayerId" UUID,
    "EndedAt" TIMESTAMPTZ(6) NOT NULL,
    "EndReason" VARCHAR(64) NOT NULL,
    "SubmissionHash" TEXT,
    "RankChangesJson" TEXT,

    CONSTRAINT "PK_Matches" PRIMARY KEY ("MatchId")
);

-- CreateTable
CREATE TABLE "game"."MatchParticipant" (
    "MatchId" UUID NOT NULL,
    "PlayerId" UUID NOT NULL,
    "DeckId" UUID NOT NULL,
    "DeckName" VARCHAR(64) NOT NULL,
    "CatalogVersion" VARCHAR(128) NOT NULL,

    CONSTRAINT "PK_MatchParticipant" PRIMARY KEY ("MatchId","PlayerId")
);

-- CreateTable
CREATE TABLE "game"."MatchDeckCard" (
    "MatchId" UUID NOT NULL,
    "PlayerId" UUID NOT NULL,
    "CardId" INTEGER NOT NULL,
    "Quantity" INTEGER NOT NULL,

    CONSTRAINT "PK_MatchDeckCard" PRIMARY KEY ("MatchId","PlayerId","CardId")
);

-- CreateIndex
CREATE INDEX "IX_RefreshSessions_FamilyId" ON "game"."RefreshSessions"("FamilyId");

-- CreateIndex
CREATE INDEX "IX_RefreshSessions_ExpiresAt" ON "game"."RefreshSessions"("ExpiresAt");

-- CreateIndex
CREATE INDEX "IX_RefreshSessions_PlayerId" ON "game"."RefreshSessions"("PlayerId");

-- CreateIndex
CREATE INDEX "IX_Decks_PlayerId" ON "game"."Decks"("PlayerId");

-- CreateIndex
CREATE INDEX "IX_BattleDeckSnapshots_PlayerId" ON "game"."BattleDeckSnapshots"("PlayerId");

-- CreateIndex
CREATE INDEX "IX_Matches_MatchId_WinnerPlayerId" ON "game"."Matches"("MatchId", "WinnerPlayerId");

-- CreateIndex
CREATE INDEX "IX_MatchParticipant_PlayerId" ON "game"."MatchParticipant"("PlayerId");

-- AddForeignKey
ALTER TABLE "game"."RefreshSessions" ADD CONSTRAINT "FK_RefreshSessions_Players_PlayerId" FOREIGN KEY ("PlayerId") REFERENCES "game"."Players"("Id") ON DELETE RESTRICT ON UPDATE NO ACTION;

-- AddForeignKey
ALTER TABLE "game"."Decks" ADD CONSTRAINT "FK_Decks_Players_PlayerId" FOREIGN KEY ("PlayerId") REFERENCES "game"."Players"("Id") ON DELETE RESTRICT ON UPDATE NO ACTION;

-- AddForeignKey
ALTER TABLE "game"."DeckCard" ADD CONSTRAINT "FK_DeckCard_Decks_DeckId" FOREIGN KEY ("DeckId") REFERENCES "game"."Decks"("Id") ON DELETE CASCADE ON UPDATE NO ACTION;

-- AddForeignKey
ALTER TABLE "game"."Ranks" ADD CONSTRAINT "FK_Ranks_Players_PlayerId" FOREIGN KEY ("PlayerId") REFERENCES "game"."Players"("Id") ON DELETE CASCADE ON UPDATE NO ACTION;

-- AddForeignKey
ALTER TABLE "game"."PromotionGame" ADD CONSTRAINT "FK_PromotionGame_Ranks_PlayerId" FOREIGN KEY ("PlayerId") REFERENCES "game"."Ranks"("PlayerId") ON DELETE CASCADE ON UPDATE NO ACTION;

-- AddForeignKey
ALTER TABLE "game"."BattleDeckSnapshots" ADD CONSTRAINT "FK_BattleDeckSnapshots_Players_PlayerId" FOREIGN KEY ("PlayerId") REFERENCES "game"."Players"("Id") ON DELETE RESTRICT ON UPDATE NO ACTION;

-- AddForeignKey
ALTER TABLE "game"."Matches" ADD CONSTRAINT "FK_Matches_MatchParticipant_MatchId_WinnerPlayerId" FOREIGN KEY ("MatchId", "WinnerPlayerId") REFERENCES "game"."MatchParticipant"("MatchId", "PlayerId") ON DELETE NO ACTION ON UPDATE NO ACTION;

-- AddForeignKey
ALTER TABLE "game"."MatchParticipant" ADD CONSTRAINT "FK_MatchParticipant_Matches_MatchId" FOREIGN KEY ("MatchId") REFERENCES "game"."Matches"("MatchId") ON DELETE CASCADE ON UPDATE NO ACTION;

-- AddForeignKey
ALTER TABLE "game"."MatchParticipant" ADD CONSTRAINT "FK_MatchParticipant_Players_PlayerId" FOREIGN KEY ("PlayerId") REFERENCES "game"."Players"("Id") ON DELETE RESTRICT ON UPDATE NO ACTION;

-- AddForeignKey
ALTER TABLE "game"."MatchDeckCard" ADD CONSTRAINT "FK_MatchDeckCard_MatchParticipant_MatchId_PlayerId" FOREIGN KEY ("MatchId", "PlayerId") REFERENCES "game"."MatchParticipant"("MatchId", "PlayerId") ON DELETE CASCADE ON UPDATE NO ACTION;

-- Preserve the EF Core checks that Prisma schema syntax cannot express.
ALTER TABLE game."DeckCard" ADD CONSTRAINT "CK_DeckCard_Quantity" CHECK ("Quantity" > 0);
ALTER TABLE game."MatchDeckCard" ADD CONSTRAINT "CK_MatchDeckCard_Quantity" CHECK ("Quantity" > 0);
ALTER TABLE game."Ranks" ADD CONSTRAINT "CK_Rank_Nonnegative" CHECK ("Division" >= 0 AND "Rp" >= 0 AND "WinStreak" >= 0);
ALTER TABLE game."PromotionGame" ADD CONSTRAINT "CK_PromotionGame_Number" CHECK ("GameNumber" > 0);

COMMIT;
