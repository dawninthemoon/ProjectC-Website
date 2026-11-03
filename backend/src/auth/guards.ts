import { CanActivate, ExecutionContext, Injectable, UnauthorizedException } from '@nestjs/common';
import { createHash, timingSafeEqual } from 'node:crypto';
import type { Request } from 'express';
import type { Player } from '../generated/prisma/client.js';
import { Settings } from '../config.js';
import { AuthService } from './auth.service.js';

export type PlayerRequest = Request & { player: Player };
@Injectable()
export class PlayerGuard implements CanActivate {
  constructor(private readonly auth: AuthService) {}
  async canActivate(context: ExecutionContext) {
    const request = context.switchToHttp().getRequest<PlayerRequest>();
    const bearer = /^Bearer\s+(.+)$/i.exec(request.headers.authorization ?? '');
    const player = bearer && await this.auth.verify(bearer[1]);
    if (!player) throw new UnauthorizedException();
    request.player = player;
    return true;
  }
}
@Injectable()
export class BattleGuard implements CanActivate {
  constructor(private readonly settings: Settings) {}
  canActivate(context: ExecutionContext) {
    const request = context.switchToHttp().getRequest<Request>();
    const key = request.headers['x-battle-api-key'];
    const occurrences = request.rawHeaders.filter((v, i) => i % 2 === 0 && v.toLowerCase() === 'x-battle-api-key').length;
    if (typeof key !== 'string' || occurrences !== 1 || !timingSafeEqual(
      createHash('sha256').update(key).digest(), createHash('sha256').update(this.settings.battleKey).digest()))
      throw new UnauthorizedException();
    return true;
  }
}
