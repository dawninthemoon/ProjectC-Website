import { Test, TestingModule } from '@nestjs/testing';
import { describe, it, expect, vi, beforeEach } from 'vitest';

import { AppController } from './app.controller.js';
import { PrismaService } from './prisma/prisma.service.js';

describe('AppController', () => {
  let appController: AppController;

  const prismaMock = {
    match: {
      findMany: vi.fn(),
    },
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    const app: TestingModule = await Test.createTestingModule({
      controllers: [AppController],
      providers: [
        {
          provide: PrismaService,
          useValue: prismaMock,
        },
      ],
    }).compile();

    appController = app.get<AppController>(AppController);
  });

  describe('getMatches', () => {
    it('should return match records', async () => {
      const matches = [
        {
          id: 'match-001',
          winnerId: 'player-1',
          loserId: 'player-2',
          createdAt: new Date(),
        },
      ];

      prismaMock.match.findMany.mockResolvedValue(matches);

      const result = await appController.getMatches();

      expect(result).toEqual(matches);
      expect(prismaMock.match.findMany).toHaveBeenCalledOnce();
    });

    it('should return an empty array when no matches exist', async () => {
      prismaMock.match.findMany.mockResolvedValue([]);

      const result = await appController.getMatches();

      expect(result).toEqual([]);
    });
  });
});