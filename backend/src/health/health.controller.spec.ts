import { jest } from '@jest/globals';
import { HttpException } from '@nestjs/common';
import { HealthController } from './health.controller.js';
import type { PrismaService } from '../prisma/prisma.service.js';

describe('DB operator health', () => {
  it('reports a successful read-only application DB check', async () => {
    const checkConnection = jest.fn<() => Promise<void>>().mockResolvedValue();
    const controller = new HealthController({ checkConnection } as unknown as PrismaService);
    expect(await controller.database()).toEqual({ status: 'ok', database: 'ok' });
  });

  it('returns 503 without leaking driver details or credentials', async () => {
    const checkConnection = jest.fn<() => Promise<void>>().mockRejectedValue(new Error('postgres://private:secret@example/db'));
    const controller = new HealthController({ checkConnection } as unknown as PrismaService);
    try {
      await controller.database();
      throw new Error('Expected health failure.');
    } catch (error) {
      expect(error).toBeInstanceOf(HttpException);
      expect((error as HttpException).getStatus()).toBe(503);
      expect((error as HttpException).getResponse()).toEqual({ status: 'unavailable', database: 'unavailable' });
    }
  });
});
