import { Body, Controller, Get, HttpCode, Post, Req, UnauthorizedException, UseGuards } from '@nestjs/common';
import { AuthService, userResponse } from './auth.service.js';
import { PlayerGuard } from './guards.js';
import type { PlayerRequest } from './guards.js';
import { object, validation } from '../common/http.js';
import { GuestRateGuard, RefreshRateGuard } from '../common/rate-limit.js';

@Controller('api')
export class AuthController {
  constructor(private readonly auth: AuthService) {}
  @Post('auth/guest') @HttpCode(200) @UseGuards(GuestRateGuard)
  guest(@Body() body: unknown) {
    const request = object(body, ['displayName']);
    if (request.displayName != null && typeof request.displayName !== 'string') validation({ displayName: ['Expected a string.'] });
    const name = (request.displayName as string | null | undefined)?.trim() ?? 'Guest';
    if (!name || name.length > 32) validation({ displayName: ['표시 이름은 1~32자여야 합니다.'] });
    return this.auth.guest(name);
  }
  @Post('auth/refresh') @HttpCode(200) @UseGuards(RefreshRateGuard)
  refresh(@Body() body: unknown) { return this.auth.rotate(this.refreshToken(body)); }
  @Post('auth/revoke') @HttpCode(204) @UseGuards(RefreshRateGuard)
  async revoke(@Body() body: unknown) { await this.auth.revoke(this.refreshToken(body)); }
  @Post('auth/session') @HttpCode(200) @UseGuards(PlayerGuard, RefreshRateGuard)
  session(@Req() request: PlayerRequest) { return this.auth.session(request.player); }
  @Get('users/me') @UseGuards(PlayerGuard)
  me(@Req() request: PlayerRequest) { return userResponse(request.player); }
  private refreshToken(body: unknown) {
    const request = object(body, ['refreshToken']);
    if (typeof request.refreshToken !== 'string' || request.refreshToken.length !== 64) throw new UnauthorizedException();
    return request.refreshToken;
  }
}
