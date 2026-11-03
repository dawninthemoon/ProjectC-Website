# NestJS 전환 결과와 배포 절차

## 운영 경로

운영 구현은 `backend/`의 TypeScript + NestJS 12 + Prisma ORM 7.10.1 + PostgreSQL 17이다.
기존 Prisma 7을 유지했으며 Prisma 8로 업그레이드하지 않았다. 테스트는 Jest 30이다.
Docker와 Dev Container는 Node.js 24를 사용한다. 로컬 검증 호스트는 Node.js 26이며 Node 24 Docker에서도 실행을 확인했다.
일반 서버 시작은 DB migration을 실행하지 않는다. Render 배포와 Neon production DB 변경은 수행하지 않았다.

## 조사 결과와 DB 소유권

- 기존 NestJS는 `public.Match`와 `GET /api/matches`만 구현되어 있었다.
  기존 Prisma migration `20261102064341_add_match`는 변경하지 않았다.
- C#의 Domain, DbContext, 두 EF migration, 인증·덱·Battle·RankCalculator 및 27개 테스트를 대조했다.
- EF는 `game` 스키마와 `game.__EFMigrationsHistory`, 기존 Prisma는 `public` 스키마와
  `public._prisma_migrations`를 사용한다. 두 이력 테이블은 서로 대체하거나 삭제하지 않는다.
- 작업 환경의 `backend/.env`, 루트 `.env`, 프로세스에는 실제 DB 조회에 사용할 DATABASE_URL/DIRECT_URL이 없었다.
  따라서 실제 Neon/기존 개발 DB 상태는 확인하지 못했다. 배포 작업자가 아래 읽기 전용 조회로 확인해야 한다.
- 새 격리 PostgreSQL 17 DB에는 Prisma migration을 적용해 구조와 제약조건을 확인했다.
  별도의 로컬 DB에는 원본 EF migration을 적용하고 Prisma baseline 절차를 검증했다.
  EF DB와 새 Prisma 모델의 구조 차이는 보존 대상인 EF 이력 테이블뿐이었다.

Prisma 모델과 물리 테이블 매핑:

| Prisma 모델 | 물리 테이블 | 의미 |
|---|---|---|
| LegacyMatch | public.Match | 기존 분석용 매치, 조회 API 유지 |
| Player | game.Players | 영속 계정 |
| RefreshToken | game.RefreshSessions | SHA-256 해시, family, 만료·폐기 시각 |
| Deck / DeckCard | game.Decks / game.DeckCard | 개인 덱과 카드 수량 |
| Rank / PromotionGame | game.Ranks / game.PromotionGame | MMR/RP와 과거 승격전 이력 |
| BattleDeckSnapshot | game.BattleDeckSnapshots | 경기 시작 전 발급한 불변 덱 |
| Match / MatchParticipant | game.Matches / game.MatchParticipant | 결과와 참가자 |
| MatchDeckSnapshot | game.MatchDeckCard | 종료 후 보존하는 카드 이력 |

UUID PK, 복합 PK, FK, 삭제 정책, 승자의 참가자 FK, 인덱스를 기존 EF와 동일한 이름으로 매핑했다.
Prisma 스키마 언어로 표현하지 못하는 네 CHECK 제약조건은 migration SQL에 유지했다.
game 테이블의 값은 서비스에서 초기화하므로 기존 EF DB에 없는 서버 기본값을 추가하지 않는다.
덱 ID는 이력 테이블에서 FK로 연결하지 않아 원본 덱 삭제 후에도 스냅샷을 보존한다.

## 새 DB 또는 기존 DB의 수동 migration

모든 명령의 작업 디렉터리는 `backend/`다. 운영 DB 삭제·초기화·`db push`는 사용하지 않는다.
먼저 백업과 staging/Neon branch에서 복원 및 연동 테스트를 수행한다.

```sh
npm ci
npm run prisma:generate
npm run prisma:validate
npm run db:inspect
```

`db:inspect`는 트랜잭션을 READ ONLY로 열고 테이블·컬럼·PK/FK/CHECK 정의만 출력한다.
연결 URL·비밀값·플레이어 데이터는 출력하지 않는다. `DIRECT_URL`이 있으면 그 연결을 우선 사용한다.

DB 상태에 따라 다음 중 하나를 선택한다.

| 확인된 DB 상태 | 수동 작업 |
|---|---|
| 빈 DB | 두 migration SQL 검토 후 `npm run prisma:migrate` |
| 기존 Prisma의 public.Match와 이력이 있고 game은 없음 | 기존 migration checksum 확인 후 `npm run prisma:migrate` |
| EF game 테이블이 이미 있고 public.Match도 기존 Prisma가 관리 | game 구조가 완전히 일치하는지 확인한 뒤 game migration만 applied로 baseline |
| EF game 테이블만 있고 public.Match는 없음 | game baseline 후 deploy하면 기존 public.Match migration만 실행됨 |
| public.Match는 있지만 Prisma 이력 없음 | 기존 Match 구조 일치 확인 후 최초 migration도 baseline |
| EF migration 일부만 적용되었거나 임의 구조 차이가 있음 | 자동 baseline 금지. staging에서 보완 SQL과 데이터 검증 절차를 별도 검토 |

game baseline 명령은 **이미 동일한 game 테이블이 존재함을 확인한 경우에만** 실행한다.
이 명령은 테이블을 만들지 않고 Prisma 이력에 이미 적용된 것으로 기록한다.

```sh
npx prisma migrate resolve --applied 20261103110000_game_persistence --config prisma.config.ts
# public.Match만 있고 Prisma 이력이 없는 경우에 한해, 구조 확인 후:
# npx prisma migrate resolve --applied 20261102064341_add_match --config prisma.config.ts
npm run prisma:migrate
```

읽기 전용 구조 비교(출력을 검토할 뿐 실행하지 않는다):

```sh
npx prisma migrate diff --from-config-datasource --to-schema prisma/schema.prisma --script --config prisma.config.ts
```

EF DB에는 `DROP TABLE "game"."__EFMigrationsHistory"`가 차이로 표시될 수 있다.
이 테이블은 Prisma 관리 대상이 아니므로 **삭제하지 않는다**. diff SQL을 DB에 자동 실행하지 않는다.
그 외 차이는 먼저 해결·검증하고 baseline 여부를 판단한다. CHECK 제약조건은 db:inspect로 별도 확인한다.
일반 Prisma deploy는 이 EF 이력 테이블을 건드리지 않는다.

## Neon과 Render

Neon은 PostgreSQL 17 프로젝트/branch를 선택하고 다음 URL을 비밀 설정으로 주입한다.

- `DATABASE_URL`: 앱의 pooled URL. Neon에서 제공한 TLS 옵션(`sslmode=require` 등)을 유지한다.
- `DIRECT_URL`: CLI migration·구조 조회용 direct URL. 앱 런타임에는 필요 없다.
- 두 URL은 같은 Neon 프로젝트·branch·DB를 가리켜야 한다. 테스트/production branch를 혼동하지 않는다.
- TLS 인증을 끄는 `rejectUnauthorized: false` 등의 우회는 구현하지 않았다.
- 트랜잭션 단위 advisory lock만 사용하므로 pooled 연결에서도 트랜잭션 동안 같은 연결에서 동작한다.
  Prisma interactive transaction은 명시적으로 ReadCommitted와 제한 시간을 설정한다.

Render는 저장소 루트를 Docker context로, `backend/Dockerfile`을 Dockerfile로 지정한다.
제공한 `render.yaml`은 Web Service 설정 초안이며 autoDeployTrigger는 off다.
자동 migration 또는 pre-deploy command는 설정하지 않았다.
사용자 승인 후 위 DB 점검·migration을 수행하고 서비스를 수동 생성/배포한다.

필수 환경 변수: `DATABASE_URL`, `JWT_SIGNING_KEY`, `BATTLE_API_KEY`.
기존 JWT를 유지하려면 signing key·issuer·audience를 바꾸지 않는다.
Render가 제공하는 `PORT`를 읽고 `0.0.0.0`에 바인딩하며 `/health`가 200을 반환한다.
HTTPS는 Render 앞단에서 종료한다. 임의의 X-Forwarded-*를 신뢰하지 않는다.
현재 요청 제한은 연결 IP 기준이어서 프록시 환경에서 여러 사용자가 제한을 공유할 수 있다.

Docker migration 이미지도 명시적 별도 작업으로 사용할 수 있다.

```sh
docker build --target migrate -f backend/Dockerfile -t projectc-migrate .
# 검토한 대상의 DIRECT_URL/DATABASE_URL만 환경 변수로 주입해 실행한다.
docker run --rm --env DATABASE_URL --env DIRECT_URL projectc-migrate
```

참고: [Neon Prisma 연결 가이드](https://github.com/neondatabase/website/blob/main/content/docs/guides/prisma.md),
[Render Blueprint 설정](https://render.com/docs/blueprint-spec), [Render Web Service](https://render.com/docs/web-services),
[Prisma 다중 스키마](https://docs.prisma.io/docs/orm/v7/prisma-schema/data-model/multi-schema).

## C# 구현과의 차이 및 보존 항목

| 항목 | 전환 후 동작/호환성 |
|---|---|
| HTTP 계약 | 모든 기존 사용자/Battle 경로·메서드·JSON 필드·성공 상태·Battle 오류 코드·인증 헤더 유지 |
| ORM | EF 대신 Prisma, 물리 game 구조 유지. 기존 public.Match도 유지 |
| JWT | 동일 HS256·issuer·audience·만료·계정 존재 검사. 일반 사용자 오차 10초, Battle 검증 0초 유지 |
| Refresh Token | 동일 32바이트 대문자 hex·SHA-256 대문자 해시·family 회전/폐기. 기존 저장 해시 사용 가능 |
| 내부 JSON | 기존 EF의 PascalCase CardsJson/RankChangesJson과 신규 camelCase 둘 다 읽음 |
| Match 해시 | C# 직렬화 속성 순서와 PascalCase·UTC 시각의 최대 7자리 소수를 유지. .NET 생성 고정 벡터로 검증 |
| 날짜 | 외부 시각은 ISO 8601이며 Nest Date 응답은 밀리초를 표시. 기존 미세초 CreatedAt의 하위 자릿수는 JS 응답에서 생략되나 DB 값을 다시 쓰지 않음 |
| 오류 본문 | Nest에서 ProblemDetails 형태로 반환. framework별 type/traceId/문구 차이는 있으며 code/status 계약은 유지 |
| JSON 입력 | 알려진 필드는 C#과 같이 대소문자 구분 없이 읽고, 모르는 필드 및 잘못된 자료형은 거부 |
| 시각 입력 | 경기 endedAt에는 명시적인 Z/offset과 최대 7자리 소수 요구. 문서화된 UTC 계약 유지. 비정상 달력 날짜도 거부 |
| 랭크 | 동일 Elo/RP 수식·경계·승격전·draw/aborted. JS 음수 반올림을 명시적으로 C#과 맞춤 |
| 동시성 | 동일한 MatchId/PlayerId/refresh family advisory lock key와 DB 트랜잭션 사용 |
| 덱 변경 | Prisma에서 부모 행 잠금과 트랜잭션으로 카드 전체 교체. 동시 삭제는 409/404로 처리 |
| 테스트 | Vitest를 Jest로 전환. 새 통합 테스트는 전용 로컬 DB를 검증하며 스키마 초기화 없이 migration 적용 |
| 운영 설정 | DATABASE_URL/Node 환경 변수 사용. 기존 Jwt__/Battle__/Refresh__/Catalog__/RateLimits__ 설정 이름도 별칭 지원 |

기능 축소나 Battle Server의 호출 변경은 없다. `/api/matches`는 기존 분석 테이블 조회이며
새 경기 결과 저장 경로와 분리한다. 승격전 상태와 최초 저장한 rankChanges 응답은 그대로 보존한다.
이미 만료된 JWT만 있고 Refresh Token/본인 확인 수단이 없는 계정은 기존 구현과 같이 복구할 수 없다.
Refresh 폐기 후 기존 Access Token이 만료까지 유효한 정책도 유지한다.

`api/ProjectC.Api`, `api/ProjectC.Api.Tests`, EF migration, ProjectC.sln, global.json,
dotnet-tools.json 및 C# Dockerfile은 자동 삭제하지 않았다. 향후 사용자가 검토 후 정리할 대상이다.
기존 api-ci 워크플로는 수동 실행만 남겨 참고 테스트를 유지한다.
기본 Compose·Dev Container·Render·자동 backend CI는 NestJS 경로만 사용한다.

## 27개 기존 테스트의 이식 대응

원본 테스트 이름을 Jest 테스트 이름에 유지했다. RankTests의 이론 테스트는 같은 개수의 cases로 실행된다.

| C# 테스트 | Jest 위치 | 검증 |
|---|---|---|
| GuestIdentityAndRankArePersistedAndProtected | test/app.e2e-spec.ts | 게스트·인증·랭크·갱신 토큰 발급 |
| DeckCrudIsScopedToTokenOwner | 동일 | CRUD·타인 덱·기존 카드 키 교체 |
| InvalidDecksAndPlayerIdInjectionAreRejectedWithoutMutation | 동일 | 카탈로그·수량·중복·null·주입·무변경 |
| InvalidTokensAreRejected | 동일 | 만료·서명·issuer·audience·미등록 사용자 |
| MatchIdIsUniqueAndDeckSnapshotSurvivesDeckDeletion | 동일 | PK 중복·참가자 카드 이력·삭제 후 보존 |
| MigrationModelHasNoPendingChanges | 동일 | migration/model 비교·CHECK 보존 |
| BattleCredentialsAreRequiredAndCannotActAsPlayer | 동일 | 서버/사용자 인증 분리·소유권·버전·구성 |
| ConcurrentRetriesAreIdempotentAndConflictDoesNotChangeRanks | 동일 | 동일 MatchId 5건 동시 처리·409·단일 반영 |
| SnapshotSurvivesDeckDeletionAndBadRequestsAreRejected | 동일 | 덱 삭제 후 저장·요청 검증·타인 snapshot |
| FailureAfterMatchInsertRollsBackMatchAndBothRanks | 동일 | 실제 DB 제약 실패 후 매치·참가자·양쪽 랭크 롤백 |
| RefreshRotatesRetainsIdentityAndReplayRevokesFamily | 동일 | 동일 계정·회전·해시 저장·계열 폐기 |
| ExpiredRefreshIsRejectedAndLegacyJwtCanAcquireSession | 동일 | Refresh 만료·기존 세션 발급·폐기 |
| ExpiredAccessTokenCanRefreshAndConcurrentRotationRevokesFamily | 동일 | Access 만료 후 갱신·동시 회전 재사용 탐지 |
| PromotionGamesPersistAcrossRequestsAndAbortedMatchLeavesRanksAlone | 동일 | 승격전 DB 저장·승급·중단 무변경 |
| DifferentMatchesWithSamePlayersSerializeRankUpdates | 동일 | 서로 다른 경기의 MMR/RP 누적 |
| EqualOpponentsHaveSymmetricRewardsAndNoStreakInflation | src/ranking/rank-calculator.spec.ts | 동급 ±20·1000경기 정체·연승 보너스 없음 |
| LowerTiersNeverLoseRp (4 cases) | 동일 | Bronze/Silver 5와 1 패배 보호 |
| MmrDifferenceAffectsBothMmrAndRp | 동일 | MMR 차이·Elo 기대값 |
| DivisionPromotionDemotionAndTierDemotionCarryRp | 동일 | RP 이월·단계/티어 강등 |
| TierBoundaryStartsBestOfFiveAndThreeWinsPromote (3 cases) | 동일 | 세 티어 경계·5전3선승 |
| FailureReturnsToEightyAndPreservesSeriesHistory | 동일 | 80RP 복귀·기록 보존·재도전 |
| TopAndBottomAreCappedAndLegacyUnrankedInitializes | 동일 | 최고/최저 경계·Unranked·draw |

추가 검증은 음수 반올림·잘못된 결과값, .NET 해시 벡터·100ns 차이, EF 내부 JSON 호환,
승격전 실패 DB 저장·무승부 제외, 기존 public.Match API와 승자 FK를 포함한다.

## 남은 운영 작업

전환 검증 결과: NestJS build·lint·Prisma validate/generate 성공, Jest 단위 17개와 PostgreSQL 17 통합 18개 통과.
원본 EF migration으로 만든 별도 DB를 baseline한 뒤에도 통합 18개가 통과했다.
Node 24 운영/migrate Docker 이미지 빌드, 새 로컬 DB migration, PORT 변경·Health·인증·덱·Battle 결과·재전송·Refresh smoke test를 검증했다.
Compose 및 Dev Container Compose 설정 검증도 통과했다. 검증은 격리된 로컬 DB에서만 수행했다.

- 실제 Neon DB 메타데이터 확인과 해당 상태에 맞는 migration/baseline, Render 배포는 수동이다.
- C++ 저장소에 문서의 HTTP 호출 순서·내구성 있는 재전송 큐를 적용해야 한다.
- 게스트 클라이언트의 안전한 Refresh 저장·회전, 카탈로그 배포 동기화와 보존 기간 정책이 필요하다.
- 개발용 Jest/CLI의 전이 의존성에는 npm audit 경고가 남아 있다. 강제 다운그레이드/호환성 불명 override는 적용하지 않았다.
  운영 의존성은 `npm audit --omit=dev`에서 취약점 0건으로 확인했다.
