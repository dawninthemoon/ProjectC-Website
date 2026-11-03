import { Module } from '@nestjs/common';
import { AppController } from './app.controller.js';
import { AppService } from './app.service.js';
import { PrismaService } from './prisma/prisma.service.js';
import { Settings } from './config.js';
import { AuthService } from './auth/auth.service.js';
import { AuthController } from './auth/auth.controller.js';
import { BattleGuard, PlayerGuard } from './auth/guards.js';
import { BattleController } from './battle/battle.controller.js';
import { BattleService } from './battle/battle.service.js';
import { DecksController } from './decks/decks.controller.js';
import { CatalogService } from './decks/catalog.service.js';
import { RankCalculator } from './ranking/rank-calculator.js';
import { RateLimits, GuestRateGuard, RefreshRateGuard } from './common/rate-limit.js';
import { HealthController } from './health/health.controller.js';

@Module({
  imports: [],
  controllers: [AppController, AuthController, DecksController, BattleController, HealthController],
  providers: [AppService, PrismaService, Settings, AuthService, BattleService, CatalogService, RankCalculator,
    BattleGuard, PlayerGuard, RateLimits, GuestRateGuard, RefreshRateGuard],
})
export class AppModule {}
