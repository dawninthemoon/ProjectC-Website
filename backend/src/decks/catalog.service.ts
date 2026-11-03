import { Injectable } from '@nestjs/common';
import { readFileSync } from 'node:fs';
import { Settings } from '../config.js';
import { object, validation } from '../common/http.js';

export interface CardEntry { cardId: number; quantity: number }
export interface SaveDeck { name: string; cards: CardEntry[] }
@Injectable()
export class CatalogService {
  readonly version: string;
  readonly requiredCardCount: number;
  readonly maximumCopiesPerCard: number;
  private readonly cards: Map<number, boolean>;
  constructor(settings: Settings) {
    const catalog = JSON.parse(readFileSync(settings.catalogPath, 'utf8')) as {
      version: string; requiredCardCount: number; maximumCopiesPerCard: number;
      cards: { cardId: number; mainDeckEligible: boolean }[];
    };
    this.version = catalog.version;
    this.requiredCardCount = catalog.requiredCardCount;
    this.maximumCopiesPerCard = catalog.maximumCopiesPerCard;
    this.cards = new Map(catalog.cards.map(c => [c.cardId, c.mainDeckEligible]));
    if (!this.version?.trim() || this.cards.size === 0 || this.requiredCardCount <= 0 || this.maximumCopiesPerCard <= 0)
      throw new Error('Invalid card catalog metadata.');
  }
  isMainDeckCard(id: number) { return this.cards.get(id) === true; }
  validate(body: unknown): SaveDeck {
    const request = object(body, ['name', 'cards']);
    const errors: Record<string, string[]> = {};
    if (typeof request.name !== 'string' || !request.name.trim() || request.name.trim().length > 64)
      errors.name = ['덱 이름은 1~64자여야 합니다.'];
    const cards = request.cards;
    if (!Array.isArray(cards) || cards.length === 0 || cards.length > this.requiredCardCount) {
      errors.cards = ['올바른 카드 목록이 필요합니다.']; validation(errors);
    }
    const issues: string[] = []; const ids = new Set<number>(); let total = 0;
    for (const value of cards) {
      if (value === null) { issues.push('카드 항목은 null일 수 없습니다.'); continue; }
      const card = object(value, ['cardId', 'quantity']);
      if (!Number.isInteger(card.cardId) || !Number.isInteger(card.quantity) ||
          (card.cardId as number) < -2147483648 || (card.cardId as number) > 2147483647 ||
          (card.quantity as number) < -2147483648 || (card.quantity as number) > 2147483647)
        validation({ cards: ['cardId and quantity must be Int32 integers.'] });
      const id = card.cardId as number, count = card.quantity as number;
      if (ids.has(id)) issues.push(`CardId ${id}: 중복 항목을 수량으로 합쳐 주세요.`);
      ids.add(id);
      if (!this.isMainDeckCard(id)) issues.push(`CardId ${id}: 존재하지 않거나 메인 덱에 사용할 수 없습니다.`);
      if (count < 1 || count > this.maximumCopiesPerCard) issues.push(`CardId ${id}: 수량은 1~${this.maximumCopiesPerCard}여야 합니다.`);
      total += count;
    }
    if (total !== this.requiredCardCount) issues.push(`덱은 정확히 ${this.requiredCardCount}장이어야 합니다.`);
    if (issues.length) errors.cards = issues;
    if (Object.keys(errors).length) validation(errors);
    return { name: (request.name as string).trim(), cards: cards as CardEntry[] };
  }
}
