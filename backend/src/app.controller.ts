import { Controller, Get } from '@nestjs/common';
import { PrismaService } from './prisma/prisma.service.js';

@Controller()
export class AppController {
  constructor(private readonly prisma: PrismaService) 
    {}

  @Get('health')
  health() { return { status: 'ok' }; }

  @Get('api/matches')
  async getMatches() {
    return this.prisma.legacyMatch.findMany();
  }
}
