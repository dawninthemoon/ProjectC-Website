# Battle Server 연동 계약 및 계정 지속성

운영 구현: `backend/` (TypeScript + NestJS + Prisma). C# 구현은 참고용으로 보존한다.
DB 인수·기능 차이·테스트 대응표는 [스택 전환 문서](stack-migration.md)를 참조한다.
이 문서는 C++ `dawninthemoon/ProjectC` Battle Server 구현자가 그대로 사용하는 HTTP 계약이다.
실시간 전투와 승패 판정은 Battle Server가 담당한다. 모든 경기 제출은 랭크 경기이며,
`Aborted`만 랭크에 반영하지 않는다. PostgreSQL `game` 스키마의 기존 모델을 확장한다.

## 인증과 공통 규약

- 운영 Base URL: 배포한 Website API의 **HTTPS** 주소. WebSocket Battle Server 주소와 구분한다.
- JSON 요청은 `Content-Type: application/json`. 필드 이름은 camelCase, GUID는 UUID 문자열,
  시간은 ISO 8601 UTC. 알 수 없는 요청 필드는 400으로 거부한다.
- 내부 API 3개는 모두 `X-Battle-Api-Key: <BATTLE_API_KEY>`가 필수다.
  사용자 JWT만으로 내부 API를 호출할 수 없으며, 서버 키만으로 개인 사용자 API를 호출할 수도 없다.
- 사용자 API는 `Authorization: Bearer <accessToken>`을 사용한다.
- Refresh Token은 인증용 비밀값이다. Battle Server에 전달하지 않는다.
- 키와 토큰을 URL, 로그, 소스, Unity 빌드, 공개 저장소에 넣지 않는다.
- 서버 키는 최소 32 UTF-8 바이트의 무작위 값이며 JWT 서명키와 달라야 한다.
  비교는 고정 시간 해시 비교다. API 시작 시 누락·짧은 키·동일 키를 거부한다.
- 신뢰하는 Battle Server가 판정한 결과만 수락한다. 결과에 PlayerId를 보냈다는 사실 자체가
  사용자 인증을 대신하지 않는다. Battle Server는 아래 인증 검증 단계를 반드시 거친다.

## 신규 엔드포인트 목록

| Method | 경로 | 인증 | 성공 |
|---|---|---|---|
| POST | `/api/internal/battle/players/verify` | 서버 키 + 본문의 사용자 JWT | 200 |
| POST | `/api/internal/battle/players/{playerId}/decks/{deckId}/validate` | 서버 키 | 200 |
| POST | `/api/internal/battle/matches` | 서버 키 | 200, 최초·재전송 동일 |
| POST | `/api/auth/refresh` | 본문의 Refresh Token | 200 |
| POST | `/api/auth/revoke` | 본문의 Refresh Token | 204 |
| POST | `/api/auth/session` | 사용자 JWT | 200 |

기존 `POST /api/auth/guest`, `GET /api/users/me`, 개인 덱 CRUD, `GET /api/rank/me` 경로는 유지한다.
guest 응답에 `refreshToken`, `refreshExpiresAt`을 추가했다. 기존 응답 필드는 유지한다.
rank 응답에는 `activePromotionSeriesId`를 추가했다.

## 1. 플레이어 JWT 검증

`POST /api/internal/battle/players/verify`

```json
{ "accessToken": "<Unity가 보낸 accessToken>" }
```

200 응답:

```json
{
  "playerId": "11111111-1111-4111-8111-111111111111",
  "displayName": "Guest",
  "createdAt": "2026-11-03T00:00:00Z"
}
```

HS256 서명, 알고리즘, 발급자, audience, 만료, DB 계정 존재를 검증한다.
내부 검증의 만료 허용 오차는 0초다. 사용자 API의 기존 JWT 허용 오차 10초는 유지한다.
401 `invalid_player_token`: 위조·만료·형식 오류·미등록 PlayerId·빈 토큰.
Battle Server는 응답의 PlayerId를 WebSocket 세션에 고정하고 클라이언트가 보낸 PlayerId를 신뢰하지 않는다.

## 2. 개인 덱 검증 및 경기 스냅샷 발급

`POST /api/internal/battle/players/{playerId}/decks/{deckId}/validate`

playerId는 검증된 세션에서 가져온다. 요청:

```json
{ "catalogVersion": "hyakki-2026-11-02-v1-fc4b30ed6ac5" }
```

200 응답 예시(실제 cards는 총 30장, cardIds는 중복을 포함한 30개 정수):

```json
{
  "snapshotId": "33333333-3333-4333-8333-333333333333",
  "playerId": "11111111-1111-4111-8111-111111111111",
  "deckId": "22222222-2222-4222-8222-222222222222",
  "catalogVersion": "hyakki-2026-11-02-v1-fc4b30ed6ac5",
  "cards": [
    { "cardId": 1001, "quantity": 3 }, { "cardId": 1002, "quantity": 3 },
    { "cardId": 1003, "quantity": 3 }, { "cardId": 1004, "quantity": 3 },
    { "cardId": 1005, "quantity": 3 }, { "cardId": 1006, "quantity": 3 },
    { "cardId": 1008, "quantity": 3 }, { "cardId": 1101, "quantity": 3 },
    { "cardId": 2001, "quantity": 3 }, { "cardId": 2002, "quantity": 3 }
  ],
  "cardIds": [1001,1001,1001,1002,1002,1002,1003,1003,1003,1004,1004,1004,1005,1005,1005,1006,1006,1006,1008,1008,1008,1101,1101,1101,2001,2001,2001,2002,2002,2002]
}
```

소유권, API·저장 덱·Battle Server의 카탈로그 버전 일치, 카드 ID/메인 덱 허용 여부,
정확히 30장·동일 카드 최대 3장 규칙을 기존 `DeckValidator`로 검사한다.
`cardIds`를 Battle Server 내부 카드 정의와 연결한다. 정렬 순서는 셔플을 대신하지 않는다.

404 `deck_not_found`: 미존재·타인 소유(정보 노출 방지를 위해 같은 응답).
409 `catalog_version_mismatch`: 누락되거나 다른 버전. 현재 카탈로그로 덱을 다시 저장하고
Battle Server와 API의 카탈로그를 맞춘 뒤 재검증한다.
422 `invalid_deck`: 저장된 덱 구성이 현재 규칙에 맞지 않는다.

발급된 스냅샷은 서버 측 DB의 불변 카드 구성이다. 경기 중 원본 덱을 수정·삭제하거나
카탈로그를 배포해도 이 경기의 결과 제출은 가능하다. 양쪽 스냅샷 버전은 같아야 한다.
발급된 snapshotId를 서버의 경기 상태에 보관한다. 경기 결과 요청에서 임의 카드 목록은 받지 않는다.
스냅샷 발급 재시도는 새 ID를 만든다. 실제 사용한 응답의 ID를 결과에 고정한다.

## 3. 경기 결과 저장

`POST /api/internal/battle/matches`

```json
{
  "matchId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
  "participants": [
    {
      "playerId": "11111111-1111-4111-8111-111111111111",
      "deckId": "22222222-2222-4222-8222-222222222222",
      "snapshotId": "33333333-3333-4333-8333-333333333333"
    },
    {
      "playerId": "44444444-4444-4444-8444-444444444444",
      "deckId": "55555555-5555-4555-8555-555555555555",
      "snapshotId": "66666666-6666-4666-8666-666666666666"
    }
  ],
  "winnerPlayerId": "11111111-1111-4111-8111-111111111111",
  "endReason": "Normal",
  "endedAt": "2026-11-03T01:00:00Z"
}
```

- MatchId는 Battle Server가 경기 생성 시 만든 UUID다. 재시도할 때 절대 바꾸지 않는다.
- 참가자는 서로 다른 2명이어야 한다. PlayerId·DeckId·SnapshotId 조합을 검증한다.
- 종료 사유: `Normal`, `Surrender`, `Disconnect`는 참가자 중 한 명의 승자가 필수다.
  `Draw`, `Aborted`는 `winnerPlayerId: null`이 필수다.
- `Draw`는 Elo 무승부로 처리하고 RP·승격전 게임 수는 바꾸지 않으며 연승은 초기화한다.
  `Aborted`는 모든 랭크 상태를 그대로 두고 경기 기록만 저장한다.
- endedAt은 필수다. 지연 제출을 허용하며 현재보다 5분 초과 미래 시각은 거부한다.
- 랭크 반영 순서는 DB에서 결과가 처리되는 순서다. 늦게 도착한 경기를 과거 상태로 소급 재계산하지 않는다.

200 응답 예시(두 참가자가 Gold 3 / 50RP, MMR 1000인 경우):

```json
{
  "matchId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
  "duplicate": false,
  "rankChanges": [
    { "playerId": "11111111-1111-4111-8111-111111111111", "mmrBefore": 1000, "mmrAfter": 1016,
      "tier": "Gold", "division": 3, "rp": 70, "rpDelta": 20, "promotionStatus": "None" },
    { "playerId": "44444444-4444-4444-8444-444444444444", "mmrBefore": 1000, "mmrAfter": 984,
      "tier": "Gold", "division": 3, "rp": 30, "rpDelta": -20, "promotionStatus": "None" }
  ]
}
```

`rankChanges` 배열 순서에 의존하지 말고 PlayerId로 대응시킨다.
같은 내용의 재전송은 200, `duplicate: true`와 최초 저장한 rankChanges를 반환한다.
참가자 순서와 시간대 표기는 정규화한다. 나머지 결과 필드는 고정해야 한다.
같은 MatchId로 결과·종료 시각·스냅샷 등을 변경하면 409 `match_result_conflict`다.
기존 모델로 직접 저장되어 해시가 없는 MatchId도 409로 처리하고 덮어쓰지 않는다.

DB 트랜잭션에서 MatchId를 잠근 뒤 참가자를 UUID 순서로 잠근다.
경기·참가자·카드 이력·승자·양쪽 MMR/RP·승격전 기록·응답을 함께 커밋한다.
실패하면 모두 롤백한다. PostgreSQL transaction-scoped advisory lock은
여러 API 인스턴스 사이에서도 유효하다. 재전송이 이후의 랭크를 다시 변경하지 않는다.

## 4. 계정 갱신·폐기

`POST /api/auth/guest` 요청은 기존 `{ "displayName": "Guest" }`다.
guest 및 아래 refresh/session의 200 응답 형식:

```json
{
  "accessToken": "<HS256 JWT>",
  "tokenType": "Bearer",
  "expiresAt": "2026-11-04T00:00:00Z",
  "user": { "playerId": "11111111-1111-4111-8111-111111111111", "displayName": "Guest", "createdAt": "2026-11-03T00:00:00Z" },
  "refreshToken": "<64자리 대문자 hex 비밀값>",
  "refreshExpiresAt": "2026-12-03T00:00:00Z"
}
```

`POST /api/auth/refresh` — JWT 불필요. 만료된 JWT가 헤더에 있어도 Refresh Token으로 갱신한다.

```json
{ "refreshToken": "<가장 최근에 발급된 refreshToken>" }
```

동일 PlayerId로 새 JWT와 새 Refresh Token을 반환한다. 덱·랭크는 변경하지 않는다.
Refresh Token은 CSPRNG 32바이트이고 DB에는 SHA-256 해시만 저장한다.
기존 Refresh Token은 즉시 폐기하며 새 토큰은 기본 30일의 유효기간을 갖는다.
잘못된·만료된·폐기된 토큰은 401. 폐기된 토큰 재사용 시 같은 family의 활성 토큰도 모두 폐기한다.
토큰 회전 및 재사용 탐지는 family별 DB 잠금으로 직렬화한다.

클라이언트는 refresh 호출을 한 번에 하나만 진행하고, 성공한 새 토큰 쌍을 안전하게 원자적으로 저장한다.
네트워크 응답 유실 후 이전 토큰을 재시도하면 보안상 family가 폐기될 수 있다.
Refresh Token은 OS 보안 저장소에 보관하고 브라우저라면 장기 토큰의 JS 노출을 피하는 별도 BFF/쿠키 설계를 사용한다.
이 API 자체는 JSON 토큰 계약이며 쿠키를 발급하지 않는다.

`POST /api/auth/revoke` — 같은 JSON 본문. JWT 불필요. 토큰 소지로 해당 family를 폐기한다.
204 본문 없음. 알 수 없는 64자리 토큰도 204, 형식 오류는 401.
폐기 후에도 이미 발급된 Access Token은 JWT 만료까지 유효하다. 즉시 Access Token 차단 목록은 구현 범위에 없다.

`POST /api/auth/session` — 유효한 사용자 Bearer JWT 필수. 본문 없음.
기존 JWT를 가진 게스트가 같은 계정의 독립적인 refresh family를 발급받는다.
기존 사용자는 JWT가 만료되기 전에 이 API를 한 번 호출해야 한다.
이미 만료된 기존 JWT만 있고 Refresh Token·외부 계정 연결·복구 비밀이 없다면 안전한 계정 복구는 불가능하다.
이 경우에도 기존 덱과 랭크 데이터는 삭제하지 않는다. PlayerId만으로 계정 복구를 허용하지 않는다.
새 Refresh Token까지 만료되거나 분실된 경우에도 별도 본인 확인 수단이 필요하다.

## 오류 및 재시도

명시적인 Battle 검증 오류 예시:

```json
{ "title": "match_result_conflict", "status": 409, "code": "match_result_conflict" }
```

| HTTP | code / 의미 | 처리 |
|---|---|---|
| 400 | `invalid_match` | 참가자·승자·종료 사유·시각 검증 실패, 요청 수정 |
| 400 | framework ValidationProblem 또는 빈 응답 | 잘못된 JSON, 자료형, 필수 본문, 알 수 없는 필드 |
| 401 | 본문 없을 수 있음 | 서버 키 누락·불일치 또는 사용자/Refresh 인증 실패 |
| 401 | `invalid_player_token` | 사용자 JWT 재발급 후 재검증 |
| 404 | `deck_not_found` | 덱 선택 재확인 |
| 409 | `catalog_version_mismatch` | 카탈로그 배포/덱 재저장 후 검증 |
| 409 | `match_result_conflict` | 자동 덮어쓰기 금지, 운영 조사 |
| 422 | `invalid_deck` | 개인 덱 수정 필요 |
| 422 | `invalid_deck_snapshot` | 참가자와 스냅샷의 조합 오류 |
| 429 | 본문 없을 수 있음 | 게스트 생성 기본 IP당 10/분, refresh·revoke·session 합계 IP당 30/분 |
| 500 / 전송 실패 | 상세 DB 오류를 클라이언트에 노출하지 않음 | 경기 결과는 같은 본문·MatchId로 지수 백오프 재전송 |

프레임워크의 ProblemDetails에는 type/traceId 등이 추가될 수 있다. 401을 모두 JSON으로 파싱한다고 가정하지 않는다.
덱·랭크를 수정하는 사용자 API는 JWT의 sub만 사용한다. 클라이언트용 결과/랭크 수정 API는 없다.

## 랭크 기본 정책

정확한 수식은 기존 구현이 없어 아래 기본값을 `backend/src/ranking/rank-calculator.ts`에 분리했다.

- 신규 계정: Bronze 5 / 0RP, MMR 1000. 기존 `Unranked`는 첫 랭크 반영 시 Bronze 5로 초기화한다.
- 티어 순서 Bronze → Silver → Gold → Platinum, 각 티어 Division 5 → 1, RP는 0..99.
- 기대 승률 `E = 1 / (1 + 10^((상대MMR - 내MMR) / 400))`.
- 결과 S: 승리 1, 패배 0, 무승부 0.5.
- `MMR += round(32 * (S - E))`. 두 선수 모두 경기 전 MMR을 기준으로 계산한다.
  MMR은 별도 바닥값 없이 정수 Elo로 유지한다.
- 승패 `RP 보상 = round(40 * (S - E))`. 반올림은 midpoint away from zero.
  동급 상대 승리 +20, 패배 -20. 강한 상대에게 이기면 증가, 약한 상대에게 지면 감소 폭이 커진다.
- Bronze/Silver는 패배 RP를 0으로 바꾼다. MMR 감소와 승격전 패배 판정은 적용한다.
- 같은 티어에서는 100RP당 다음 Division으로 올리고 초과분을 이월한다.
  음수 RP는 이전 Division으로 이월: Gold 3 / 3RP -5 → Gold 4 / 98RP.
- Gold 5 아래는 Silver 1로 강등될 수 있다. 이후 Silver 패배 RP 보호를 받는다.
- Division 1에서 100RP 도달 시 99RP에 고정하고 다음 경기부터 승격전을 시작한다.
  초과 RP는 이월하지 않는다. 승격전 3승은 다음 티어 5 / 0RP, 3패는 기존 티어 1 / 80RP.
  무승부·중단은 승격전 판수에 포함하지 않는다. 과거 series별 기록은 보존한다.
- Platinum 1은 99RP 상한이며 추가 승격전은 없다.
- 연승 횟수는 기록하지만 RP 보너스는 지급하지 않는다. Gold 이상에서 동급 상대 50% 승률은
  대칭 보상으로 장기 정체하도록 설계했다. MMR 차이와 경계·승격전 때문에 개별 구간은 달라질 수 있다.
- 응답의 rpDelta는 계산된 경기 보상이며 승급/강등 후 RP 수치의 단순 차이가 아니다.
  승격전·무승부·패배 보호에서는 0이다. 상한에서 보상 일부가 소멸할 수 있다.
- promotionStatus: `None`, `InProgress`, `Succeeded`, `Failed`. 종료 상태는 마지막 승격전 결과를 유지한다.
  진행 중인 series 식별자는 사용자 rank 응답의 activePromotionSeriesId다.

## Battle Server 연동 순서

1. Unity는 guest 응답의 토큰 쌍을 저장한다. 필요시 refresh로 같은 계정을 유지한다.
2. WebSocket 접속 시 Access Token과 DeckId만 Battle Server에 전달한다.
3. Battle Server는 서버 키로 players/verify를 호출하고 응답 PlayerId를 세션에 고정한다.
4. 양쪽 플레이어에 대해 덱 검증 API를 호출한다. Battle Server 카탈로그 버전을 반드시 전달한다.
5. 두 응답의 버전 일치를 확인하고 cardIds로 전투를 구성한다. snapshotId·DeckId·PlayerId를 보관한다.
6. Battle Server가 생성한 고정 MatchId로 전투를 진행하고 종료 시 결과를 자신의 내구성 있는 재전송 큐에 기록한다.
7. matches에 결과를 보낸다. 200이면 큐에서 완료 처리한다. 응답 유실/5xx는 동일 본문으로 재시도한다.
8. 클라이언트는 GET /api/rank/me로 최신 랭크를 확인한다.

## 환경 변수와 수동 배포

| API 환경 변수 | 필수/기본값 | 의미 |
|---|---|---|
| `NODE_ENV` | 운영 `production` | NestJS 운영 모드 |
| `DATABASE_URL` | 필수 | PostgreSQL URL. Neon 앱은 pooled URL과 TLS 옵션 사용 |
| `DIRECT_URL` | migration/조회 시 권장 | Neon direct URL, 같은 branch/DB를 지정 |
| `JWT_SIGNING_KEY` | 필수, 최소 32바이트 | 기존 서명키 유지 |
| `JWT_ISSUER` | `ProjectC.Website` | JWT 발급자 |
| `JWT_AUDIENCE` | `ProjectC.Client` | JWT audience |
| `JWT_LIFETIME_MINUTES` | `1440`, 1..10080 | Access Token 수명 |
| `BATTLE_API_KEY` | 필수, 최소 32바이트 | API와 Battle Server에만 동일 비밀 주입 |
| `REFRESH_LIFETIME_DAYS` | `30`, 1..365 | 회전된 Refresh Token 수명 |
| `GUEST_PERMIT_LIMIT` | `10` | IP당 게스트 생성/분 |
| `CATALOG_PATH` | `catalog/cards.json` | backend 작업 디렉터리 기준 파일 |
| `PORT` | `8080` | Render 제공 값을 읽고 0.0.0.0 바인딩 |
| `PROJECTC_TEST_DATABASE` | 통합 테스트 시 필수 | 이름에 test가 포함된 전용 로컬 PostgreSQL URL |

기존 `Jwt__SigningKey`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__LifetimeMinutes`, `Battle__ApiKey`,
`Refresh__LifetimeDays`, `RateLimits__GuestPermitLimit`, `Catalog__Path`도 별칭으로 지원한다.
새 변수 이름을 우선 사용하며 `ConnectionStrings__GameDatabase` 대신 PostgreSQL URL을 사용한다.

Docker Compose 루트 .env에는 POSTGRES_USER, POSTGRES_DB, POSTGRES_PASSWORD 및 두 인증 키를 설정한다.
비밀번호는 URL에 안전한 무작위 hex 값을 권장한다. npm 실행은 backend/.env.example을 참고한다.
Battle Server에는 API HTTPS Base URL과 동일한 서버 키를 해당 저장소의 환경 변수 이름에 맞춰 주입한다.
이 저장소는 C++ 코드나 Render 서비스를 자동 변경하지 않는다.

수동 작업:

1. `npm run db:inspect`로 DB 구조를 읽기 전용 확인하고 백업한다.
2. [DB 상태별 migration/baseline 절차](stack-migration.md)를 따른다.
   기존 EF game 테이블이 있는 DB에 CREATE TABLE migration을 그대로 실행하지 않는다.
3. staging/Neon branch에서 검증 후 승인된 작업자가 `npm run prisma:migrate`를 실행한다.
   일반 서버 시작은 migration을 실행하지 않는다. 운영 DB reset/db push는 사용하지 않는다.
4. Render에 backend/Dockerfile, HTTPS, 환경 변수와 /health를 설정하고 수동 배포한다.
5. Battle Server의 인증·덱 검증·결과 제출·재전송 큐를 연결한다.
6. 게스트 클라이언트에 session 발급과 안전한 Refresh 저장을 적용한다.

서버 키 교체는 양쪽 설정을 맞춘 유지보수로 수행한다. 현재 동시 복수 키 허용은 없다.
매치 hash/응답/승격전 이력은 멱등성·감사를 위해 보존한다. 스냅샷은 지연 제출이 필요 없는 시점까지
보존해야 하며 자동 삭제는 없다. Refresh 재사용 탐지를 위해 폐기된 부모 토큰 행을 활성 자식이
있는 동안 삭제하면 안 된다. 보존 기간·정리 작업은 운영 정책으로 별도 결정한다.

## 검증

전용 PostgreSQL 17 DB URL을 `PROJECTC_TEST_DATABASE`로 설정한다. 작업 디렉터리는 backend다.

```sh
npm ci
npm run prisma:generate
npm run build
npm test
npm run test:e2e
```

테스트는 실제 Prisma migration을 적용하며 스키마를 초기화하지 않는다.
DB 이름에 test가 없거나 허용한 로컬 호스트가 아니면 통합 테스트를 거부한다.
기존 27개 검증 항목의 Jest 대응표와 추가 호환성 검증은 [스택 전환 문서](stack-migration.md)에 있다.

구현 참고: [Prisma 다중 스키마](https://docs.prisma.io/docs/orm/v7/prisma-schema/data-model/multi-schema),
[Render Web Services](https://render.com/docs/web-services).
