import { CanActivate, ExecutionContext, HttpException, Injectable, OnModuleDestroy } from '@nestjs/common';
import type { Request } from 'express';
import { Settings } from '../config.js';

@Injectable()
export class RateLimits implements OnModuleDestroy {
  private readonly windows = new Map<string, { count: number; until: number }>();
  private readonly cleanup = setInterval(() => {
    for (const [key, value] of this.windows) if (value.until <= Date.now()) this.windows.delete(key);
  }, 60000).unref();
  constructor(private readonly settings: Settings) {}
  check(request: Request, policy: 'guest' | 'refresh') {
    const key = `${policy}:${request.socket.remoteAddress ?? 'unknown'}`;
    const now = Date.now();
    let window = this.windows.get(key);
    if (!window || window.until <= now) { window = { count: 0, until: now + 60000 }; this.windows.set(key, window); }
    if (++window.count > (policy === 'guest' ? this.settings.guestLimit : 30)) throw new HttpException('', 429);
  }
  onModuleDestroy() { clearInterval(this.cleanup); }
}
@Injectable()
export class GuestRateGuard implements CanActivate {
  constructor(private readonly limits: RateLimits) {}
  canActivate(context: ExecutionContext) { this.limits.check(context.switchToHttp().getRequest(), 'guest'); return true; }
}
@Injectable()
export class RefreshRateGuard implements CanActivate {
  constructor(private readonly limits: RateLimits) {}
  canActivate(context: ExecutionContext) { this.limits.check(context.switchToHttp().getRequest(), 'refresh'); return true; }
}
