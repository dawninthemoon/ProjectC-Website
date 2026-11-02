import { Controller, Get } from '@nestjs/common';
import { PrismaService } from './prisma/prisma.service.js';

@Controller('api')
export class AppController {
  constructor(private readonly prisma: PrismaService) 
    {}

  @Get('matches')
  async getMatches() {
    return this.prisma.match.findMany();
  }
}
