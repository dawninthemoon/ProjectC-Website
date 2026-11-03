import { Body, Controller, Delete, Get, HttpCode, NotFoundException, Param, Post, Put, Req, Res, UseGuards } from '@nestjs/common';
import type { Response } from 'express';
import { PlayerGuard } from '../auth/guards.js';
import type { PlayerRequest } from '../auth/guards.js';
import { PrismaService } from '../prisma/prisma.service.js';
import { CatalogService } from './catalog.service.js';
import { GuidPipe } from '../common/http.js';
import type { Deck, DeckCard } from '../generated/prisma/client.js';

const deckResponse = (d: Deck & { cards: DeckCard[] }) => ({ id: d.id, name: d.name, catalogVersion: d.catalogVersion,
  createdAt: d.createdAt, updatedAt: d.updatedAt, cards: [...d.cards].sort((a, b) => a.cardId - b.cardId)
    .map(c => ({ cardId: c.cardId, quantity: c.quantity })) });

@Controller('api') @UseGuards(PlayerGuard)
export class DecksController {
  constructor(private readonly db: PrismaService, private readonly catalog: CatalogService) {}
  @Get('decks')
  async list(@Req() request: PlayerRequest) {
    return (await this.db.deck.findMany({ where: { playerId: request.player.id }, include: { cards: true },
      orderBy: [{ createdAt: 'asc' }, { id: 'asc' }] })).map(deckResponse);
  }
  @Post('decks') @HttpCode(201)
  async create(@Body() body: unknown, @Req() request: PlayerRequest, @Res({ passthrough: true }) response: Response) {
    const valid = this.catalog.validate(body);
    const now = new Date();
    const deck = await this.db.deck.create({ data: { playerId: request.player.id, name: valid.name,
      catalogVersion: this.catalog.version, createdAt: now, updatedAt: now, cards: { create: valid.cards } }, include: { cards: true } });
    response.location('/api/decks');
    return deckResponse(deck);
  }
  @Put('decks/:id')
  async update(@Param('id', GuidPipe) id: string, @Body() body: unknown, @Req() request: PlayerRequest) {
    return this.db.$transaction(async tx => {
      const deck = await tx.deck.findFirst({ where: { id, playerId: request.player.id } });
      if (!deck) throw new NotFoundException();
      const valid = this.catalog.validate(body);
      // Lock the parent row before replacing cards; concurrent edits cannot interleave.
      await tx.$queryRaw`SELECT "Id" FROM game."Decks" WHERE "Id" = ${id}::uuid FOR UPDATE`;
      await tx.deckCard.deleteMany({ where: { deckId: id } });
      return deckResponse(await tx.deck.update({ where: { id, playerId: request.player.id }, data: {
        name: valid.name, catalogVersion: this.catalog.version, updatedAt: new Date(), cards: { create: valid.cards },
      }, include: { cards: true } }));
    });
  }
  @Delete('decks/:id') @HttpCode(204)
  async delete(@Param('id', GuidPipe) id: string, @Req() request: PlayerRequest) {
    const deleted = await this.db.deck.deleteMany({ where: { id, playerId: request.player.id } });
    if (!deleted.count) throw new NotFoundException();
  }
  @Get('rank/me')
  async rank(@Req() request: PlayerRequest) {
    const { playerId: _, ...rank } = await this.db.rank.findUniqueOrThrow({ where: { playerId: request.player.id },
      include: { promotionGames: { orderBy: [{ seriesId: 'asc' }, { gameNumber: 'asc' }] } } });
    return { ...rank, promotionGames: rank.promotionGames.map(g => ({ seriesId: g.seriesId, gameNumber: g.gameNumber, won: g.won })) };
  }
}
