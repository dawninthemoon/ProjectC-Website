import 'dotenv/config';

import {
  Injectable,
  OnModuleDestroy,
} from '@nestjs/common';

import { PrismaClient } from '../generated/prisma/client.js';
import { PrismaPg } from '@prisma/adapter-pg';

@Injectable()
export class PrismaService
  extends PrismaClient
  implements OnModuleDestroy
{
  constructor() {
    const connectionString = process.env.DATABASE_URL;

    if (!connectionString) {
      throw new Error('DATABASE_URL is not configured');
    }

    const adapter = new PrismaPg({
      connectionString,
      // Bound connection establishment, including Neon compute wake-up.
      connectionTimeoutMillis: 10000,
    });

    super({ adapter });
  }

  async checkConnection(): Promise<void> {
    await this.$transaction(async tx => {
      await tx.$executeRaw`SET TRANSACTION READ ONLY`;
      await tx.$executeRaw`SELECT set_config('statement_timeout', '5000', true)`;
      await tx.$queryRaw`SELECT 1 AS ok`;
    }, { maxWait: 3000, timeout: 10000 });
  }

  async onModuleDestroy(): Promise<void> {
    await this.$disconnect();
  }
}
