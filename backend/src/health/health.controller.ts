import { Controller, Get, HttpException, UseGuards } from '@nestjs/common';
import { BattleGuard } from '../auth/guards.js';
import { PrismaService } from '../prisma/prisma.service.js';

@Controller('health')
export class HealthController {
  constructor(private readonly prisma: PrismaService) {}

  // An operator diagnostic: do not wake Neon on every public liveness probe.
  @Get('db')
  @UseGuards(BattleGuard)
  async database() {
    try {
      await this.prisma.checkConnection();
      return { status: 'ok', database: 'ok' };
    } catch {
      // Never expose connection URLs, credentials, topology, or driver errors.
      throw new HttpException({ status: 'unavailable', database: 'unavailable' }, 503);
    }
  }
}
