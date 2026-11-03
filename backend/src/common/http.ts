import { ArgumentsHost, Catch, ExceptionFilter, HttpException, Injectable, PipeTransform } from '@nestjs/common';
import type { Response } from 'express';

export function problem(status: number, code: string): never {
  throw new HttpException({ title: code, status, code }, status);
}
export function validation(errors: Record<string, string[]>): never {
  throw new HttpException({ title: 'One or more validation errors occurred.', status: 400, errors }, 400);
}
export function object(value: unknown, allowed: string[]): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) validation({ body: ['Expected a JSON object.'] });
  const result: Record<string, unknown> = {};
  for (const [key, field] of Object.entries(value)) {
    const canonical = allowed.find(k => k.toLowerCase() === key.toLowerCase());
    if (!canonical) validation({ body: ['Unknown field.'] });
    result[canonical] = field;
  }
  return result;
}
export const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function uuid(value: unknown): value is string {
  return typeof value === 'string' && uuidPattern.test(value) && value !== '00000000-0000-0000-0000-000000000000';
}
@Injectable()
export class GuidPipe implements PipeTransform<string> {
  transform(value: string) {
    if (!uuidPattern.test(value)) throw new HttpException('Not Found', 404);
    return value.toLowerCase();
  }
}

@Catch()
export class ApiExceptionFilter implements ExceptionFilter {
  catch(error: unknown, host: ArgumentsHost) {
    const response = host.switchToHttp().getResponse<Response>();
    if (error instanceof HttpException) {
      const status = error.getStatus();
      if (status === 401 || status === 429 || status === 404) {
        const body = error.getResponse();
        if (typeof body !== 'object' || !('code' in body)) { response.status(status).end(); return; }
      }
      const body = error.getResponse();
      response.status(status).json(typeof body === 'object' ? body : { title: body, status });
      return;
    }
    // Prisma concurrent mutation of an already-deleted record.
    if (error && typeof error === 'object' && 'code' in error && error.code === 'P2025') {
      response.status(409).json({ status: 409, title: 'Data changed. Read again and retry.' }); return;
    }
    // Do not expose SQL, tokens, connection strings, or driver details.
    response.status(500).json({ status: 500, title: 'Internal Server Error' });
  }
}
