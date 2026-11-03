# ProjectC Website API

운영 서버는 **TypeScript + NestJS 12 + Prisma 7 + PostgreSQL 17**입니다.
Unity 계정·개인 덱·랭크와 C++ Battle Server의 영속 데이터를 제공합니다.
테스트는 Jest, 배포 대상은 Neon PostgreSQL + Render Web Service입니다.

- [API 계약·JSON·인증·연동 순서](docs/battle-server-api.md)
- [스택 전환 결과·DB 인수·Neon/Render 수동 배포](docs/stack-migration.md)
- [NestJS 개발·테스트 명령](backend/README.md)

```text
backend/
  src/auth/       JWT, Refresh Token, 사용자·서버 인증 분리
  src/decks/      카탈로그 검증·소유권 기반 CRUD
  src/battle/     불변 덱 스냅샷·멱등 결과 저장·트랜잭션
  src/ranking/    Elo MMR·RP·승격전 계산과 단위 테스트
  prisma/         public/game 모델·migration
  catalog/        카드 ID·덱 규칙·카탈로그 버전
  test/           PostgreSQL 통합 테스트
  Dockerfile      Node 24 빌드·명시적 migration·운영 이미지
api/              기존 C# 구현·테스트·EF migration (참고용 보존)
```

## 로컬 Docker 실행

1. .env.example을 .env로 복사합니다. 기존 파일과 DB 비밀번호는 보존합니다.
2. `openssl rand -hex 32`로 POSTGRES_PASSWORD, JWT_SIGNING_KEY, BATTLE_API_KEY를 각각 생성합니다.
3. 기존 볼륨이 있으면 먼저 DB 구조를 확인하고 문서의 migration/baseline 절차를 따릅니다.
   EF game 테이블이 이미 있다면 아래 Compose 시작 전에 인수 절차가 필요합니다.
4. 새 로컬 DB의 경우 다음 명령으로 실행합니다.

```sh
docker compose up --build -d
curl http://localhost:8080/health
docker compose logs migrate api
```

Compose는 기존 postgres_data 볼륨을 유지하고 별도 migrate 작업 성공 후 NestJS를 시작합니다.
`docker compose down`은 볼륨을 보존합니다. 데이터가 있는 환경에서 down -v를 사용하지 마세요.
운영 Render 배포와 Neon production migration은 자동 수행하지 않습니다.

Dev Container는 Node 24와 기존 로컬 PostgreSQL을 사용하며 작업 위치는 /workspace/backend입니다.

## API 개요

- `POST /api/auth/guest`, `/api/auth/refresh`, `/api/auth/revoke`, `/api/auth/session`
- `GET /api/users/me`, `GET /api/rank/me`
- `GET/POST /api/decks`, `PUT/DELETE /api/decks/{id}`
- `POST /api/internal/battle/players/verify`
- `POST /api/internal/battle/players/{playerId}/decks/{deckId}/validate`
- `POST /api/internal/battle/matches`
- `GET /health`, 기존 분석용 `GET /api/matches`

사용자 API는 Bearer JWT, 내부 Battle API는 X-Battle-Api-Key를 사용합니다.
Refresh Token은 해시 저장·회전·재사용 탐지를 지원합니다.
결과 저장은 MatchId와 참가자 잠금, DB 트랜잭션으로 중복 랭크 반영을 막습니다.
랭크는 Elo K=32, 동급 RP ±20, Silver 이하 패배 보호, 5전3선승 승격전을 적용합니다.

기존 게스트는 JWT 만료 전에 /api/auth/session으로 Refresh Token을 발급받을 수 있습니다.
이미 만료된 JWT만 있고 복구 수단이 없는 계정은 본인 확인이 불가능하며 기존 데이터는 삭제하지 않습니다.

카드 카탈로그 갱신은 다음과 같이 수행합니다.

```sh
python scripts/export-card-catalog.py /path/to/ProjectC --ref <검증한 커밋>
```

원본 카드 효과·스탯·이미지는 복제하지 않습니다. 변경된 backend/catalog/cards.json을 검토·테스트하고
API와 Battle Server에 동일한 카탈로그 버전을 배포합니다.
