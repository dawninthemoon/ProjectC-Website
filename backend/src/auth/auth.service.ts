import { Injectable, UnauthorizedException } from '@nestjs/common';
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import jwt from 'jsonwebtoken';
import { Settings } from '../config.js';
import { PrismaService } from '../prisma/prisma.service.js';
import type { Player, Prisma } from '../generated/prisma/client.js';
import { uuid } from '../common/http.js';

export const tokenHash = (value: string) => createHash('sha256').update(value).digest('hex').toUpperCase();
export const userResponse = (player: Player) => ({ playerId: player.id, displayName: player.displayName, createdAt: player.createdAt });
export const initialRank = () => ({ mmr: 1000, tier: 'Bronze', division: 5, rp: 0, winStreak: 0, promotionStatus: 'None', activePromotionSeriesId: null });

@Injectable()
export class AuthService {
  constructor(private readonly db: PrismaService, private readonly settings: Settings) {}

  async issue(tx: Prisma.TransactionClient, player: Player, familyId: string = randomUUID()) {
    const now = Date.now();
    const expiresAt = new Date(now + this.settings.jwtMinutes * 60000);
    const refreshExpiresAt = new Date(now + this.settings.refreshDays * 86400000);
    const refreshToken = randomBytes(32).toString('hex').toUpperCase();
    const accessToken = jwt.sign({ sub: player.id, jti: randomUUID(), nbf: Math.floor(now / 1000),
      exp: Math.floor(expiresAt.getTime() / 1000) }, this.settings.jwtKey,
      { algorithm: 'HS256', issuer: this.settings.issuer, audience: this.settings.audience });
    await tx.refreshToken.create({ data: { tokenHash: tokenHash(refreshToken), playerId: player.id, familyId,
      expiresAt: refreshExpiresAt, revokedAt: null } });
    return { accessToken, tokenType: 'Bearer', expiresAt, user: userResponse(player), refreshToken, refreshExpiresAt };
  }

  async verify(token: unknown, tolerance = 10): Promise<Player | null> {
    if (typeof token !== 'string' || !token.trim() || token.length > 16384) return null;
    let subject: string;
    try {
      const claims = jwt.verify(token, this.settings.jwtKey, { algorithms: ['HS256'], issuer: this.settings.issuer,
        audience: this.settings.audience, clockTolerance: tolerance });
      if (typeof claims === 'string' || typeof claims.exp !== 'number' || !uuid(claims.sub) ||
          (typeof claims.nbf === 'number' && claims.nbf > claims.exp)) return null;
      subject = claims.sub.toLowerCase();
    } catch { return null; }
    // A DB outage is a server error, not an invalid credential.
    return this.db.player.findUnique({ where: { id: subject } });
  }

  guest(displayName: string) {
    return this.db.$transaction(async tx => {
      const player = await tx.player.create({ data: { displayName, createdAt: new Date(), rank: { create: initialRank() } } });
      return this.issue(tx, player);
    });
  }

  async rotate(token: string) {
    const result = await this.db.$transaction(async tx => {
      const hash = tokenHash(token);
      const first = await tx.refreshToken.findUnique({ where: { tokenHash: hash } });
      if (!first) return null;
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(hashtextextended(${first.familyId}, 0))`;
      const session = await tx.refreshToken.findUniqueOrThrow({ where: { tokenHash: hash } });
      if (session.revokedAt) {
        await this.revokeFamily(tx, session.familyId);
        return null; // Commit revocation before returning 401.
      }
      if (session.expiresAt.getTime() <= Date.now()) return null;
      await tx.refreshToken.update({ where: { tokenHash: hash }, data: { revokedAt: new Date() } });
      const player = await tx.player.findUniqueOrThrow({ where: { id: session.playerId } });
      return this.issue(tx, player, session.familyId);
    }, { isolationLevel: 'ReadCommitted', timeout: 15000 });
    if (!result) throw new UnauthorizedException();
    return result;
  }

  revoke(token: string) {
    return this.db.$transaction(async tx => {
      const session = await tx.refreshToken.findUnique({ where: { tokenHash: tokenHash(token) } });
      if (!session) return;
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(hashtextextended(${session.familyId}, 0))`;
      await this.revokeFamily(tx, session.familyId);
    }, { isolationLevel: 'ReadCommitted', timeout: 15000 });
  }
  private revokeFamily(tx: Prisma.TransactionClient, familyId: string) {
    return tx.refreshToken.updateMany({ where: { familyId, revokedAt: null }, data: { revokedAt: new Date() } });
  }
  session(player: Player) { return this.db.$transaction(tx => this.issue(tx, player)); }
}
