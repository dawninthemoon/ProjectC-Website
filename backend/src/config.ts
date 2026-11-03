import 'dotenv/config';
import { Injectable } from '@nestjs/common';

@Injectable()
export class Settings {
  readonly jwtKey = process.env.JWT_SIGNING_KEY ?? process.env.Jwt__SigningKey ?? '';
  readonly battleKey = process.env.BATTLE_API_KEY ?? process.env.Battle__ApiKey ?? '';
  readonly issuer = process.env.JWT_ISSUER ?? process.env.Jwt__Issuer ?? 'ProjectC.Website';
  readonly audience = process.env.JWT_AUDIENCE ?? process.env.Jwt__Audience ?? 'ProjectC.Client';
  readonly jwtMinutes = integer(process.env.JWT_LIFETIME_MINUTES ?? process.env.Jwt__LifetimeMinutes, 1440, 1, 10080);
  readonly refreshDays = integer(process.env.REFRESH_LIFETIME_DAYS ?? process.env.Refresh__LifetimeDays, 30, 1, 365);
  readonly guestLimit = integer(process.env.GUEST_PERMIT_LIMIT ?? process.env.RateLimits__GuestPermitLimit, 10, 1, 100000);
  readonly catalogPath = process.env.CATALOG_PATH ?? process.env.Catalog__Path ?? 'catalog/cards.json';

  constructor() {
    if (Buffer.byteLength(this.jwtKey) < 32 || Buffer.byteLength(this.battleKey) < 32 ||
        this.jwtKey === this.battleKey || !this.issuer.trim() || !this.audience.trim())
      throw new Error('Configure distinct JWT_SIGNING_KEY and BATTLE_API_KEY (32+ bytes), issuer and audience.');
  }
}

function integer(value: string | undefined, fallback: number, min: number, max: number) {
  const result = value === undefined ? fallback : Number(value);
  if (!Number.isInteger(result) || result < min || result > max) throw new Error('Invalid numeric environment setting.');
  return result;
}
