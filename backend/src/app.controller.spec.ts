import { Test, TestingModule } from '@nestjs/testing';
import { describe, it, expect, jest, beforeEach } from '@jest/globals';

import { AppController } from './app.controller.js';
import { PrismaService } from './prisma/prisma.service.js';

describe('AppController', () => {
  let appController: AppController;

  const prismaMock = {
    legacyMatch: {
      findMany: jest.fn<() => Promise<unknown[]>>(),
    },
  };

  beforeEach(async () => {
    jest.clearAllMocks();

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

      prismaMock.legacyMatch.findMany.mockResolvedValue(matches);

      const result = await appController.getMatches();

      expect(result).toEqual(matches);
      expect(prismaMock.legacyMatch.findMany).toHaveBeenCalledTimes(1);
    });

    it('should return an empty array when no matches exist', async () => {
      prismaMock.legacyMatch.findMany.mockResolvedValue([]);

      const result = await appController.getMatches();

      expect(result).toEqual([]);
    });
  });
});
