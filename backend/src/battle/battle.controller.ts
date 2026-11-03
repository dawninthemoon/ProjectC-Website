import { Body, Controller, HttpCode, Param, Post, UseGuards } from '@nestjs/common';
import { BattleGuard } from '../auth/guards.js';
import { AuthService, userResponse } from '../auth/auth.service.js';
import { GuidPipe, object, problem } from '../common/http.js';
import { BattleService } from './battle.service.js';

@Controller('api/internal/battle') @UseGuards(BattleGuard)
export class BattleController {
  constructor(private readonly auth: AuthService, private readonly battle: BattleService) {}
  @Post('players/verify') @HttpCode(200)
  async verify(@Body() body: unknown) {
    const request = object(body, ['accessToken']);
    const player = await this.auth.verify(request.accessToken, 0);
    if (!player) problem(401, 'invalid_player_token');
    return userResponse(player);
  }
  @Post('players/:playerId/decks/:deckId/validate') @HttpCode(200)
  validate(@Param('playerId', GuidPipe) playerId: string, @Param('deckId', GuidPipe) deckId: string, @Body() body: unknown) {
    return this.battle.validateDeck(playerId, deckId, object(body, ['catalogVersion']).catalogVersion);
  }
  @Post('matches') @HttpCode(200)
  submit(@Body() body: unknown) { return this.battle.submit(body); }
}
