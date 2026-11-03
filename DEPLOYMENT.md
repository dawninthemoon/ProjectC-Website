# Neon PostgreSQL 17 + Render 배포 절차

## 대상과 현재 상태

- Neon Project ID: `still-grass-68660241`
- Branch: `production`
- PostgreSQL: `17`
- 운영 API: `backend/`의 TypeScript + NestJS 12 + Prisma ORM 7.10.1
- 운영 컨테이너: Node.js 24, `node dist/main.js`. ASP.NET Core/EF Core 런타임 의존성 없음.
- C# 코드·테스트·EF migration은 참고용 보존. Compose·Render·기본 CI는 NestJS를 사용한다.

프로젝트 ID는 접속 호스트가 아니다. Neon의 **Connect** 화면에서 해당 production 브랜치의
DB와 role을 선택해 실제 pooled/direct URL을 가져와야 한다. 이 저장소에는 URL·비밀번호를 넣지 않는다.
현재 실제 Neon 연결 문자열이 제공되지 않았으므로 production 접속·테이블 상태는 미검증이다.
이 작업에서는 로컬 PostgreSQL 17만 변경했으며 Neon production migration과 Render 배포는 수행하지 않았다.

## 1. API 계약 점검

`docs/battle-server-api.md`와 컨트롤러 구현을 대조했으며 **누락된 기존 엔드포인트는 없다**.
응답 필드·상태 코드·Bearer JWT와 X-Battle-Api-Key 헤더를 유지한다.

| Method | 경로 | 인증 | 정상 응답 |
|---|---|---|---|
| POST | /api/auth/guest | 공개 | 200, access/refresh token과 user |
| POST | /api/auth/refresh | 본문의 Refresh Token | 200, 회전된 토큰 쌍 |
| POST | /api/auth/revoke | 본문의 Refresh Token | 204 |
| POST | /api/auth/session | 사용자 Bearer JWT | 200, 동일 계정 세션 |
| GET | /api/users/me | 사용자 Bearer JWT | 200 |
| GET | /api/decks | 사용자 Bearer JWT | 200 |
| POST | /api/decks | 사용자 Bearer JWT | 201 |
| PUT | /api/decks/{id} | 사용자 Bearer JWT | 200 |
| DELETE | /api/decks/{id} | 사용자 Bearer JWT | 204 |
| GET | /api/rank/me | 사용자 Bearer JWT | 200 |
| POST | /api/internal/battle/players/verify | 서버 키 + 본문의 사용자 JWT | 200 |
| POST | /api/internal/battle/players/{playerId}/decks/{deckId}/validate | 서버 키 | 200 |
| POST | /api/internal/battle/matches | 서버 키 | 200, 최초·중복 제출 |
| GET | /api/matches | 기존 공개 분석 API | 200, public.Match 배열 |
| GET | /health | 공개 | 200, 기존 `{ "status": "ok" }` |
| GET | /health/db | 서버 키, 이번에 추가 | 200 또는 503 |

개인 덱·랭크는 JWT의 사용자만 접근한다. 경기 결과 저장은 서버 키만 허용하며 동일 MatchId의
다른 내용은 409, 동일 내용은 최초 응답 반환, 매치·양쪽 랭크는 같은 트랜잭션으로 처리한다.
상세 JSON·오류 코드·C++ 호출 순서는 [API 계약](docs/battle-server-api.md)을 따른다.

## 2. 필요한 환경 변수

앱은 `DATABASE_URL`, Prisma CLI는 `DIRECT_URL` 우선으로 연결한다.
로컬에서는 DIRECT_URL을 생략할 수 있으나 **Neon migration 작업에는 direct URL을 명시한다**.

| 이름 | 값/기본값 | 설정 위치 |
|---|---|---|
| DATABASE_URL | Neon production pooled PostgreSQL URL | Render Secret, 로컬 backend/.env |
| DIRECT_URL | 같은 production DB/role의 direct URL | migration 담당 환경, 필요시 점검 환경 |
| JWT_SIGNING_KEY | 무작위 최소 32바이트, 기존 키가 있으면 유지 | Render Secret |
| BATTLE_API_KEY | JWT 키와 다른 무작위 최소 32바이트 | Render Secret + Battle Server Secret |
| NODE_ENV | production | Render |
| PORT | Render가 주입, 로컬 기본 8080 | Render 자동 제공 |
| JWT_ISSUER | ProjectC.Website | 기존 토큰과 동일하게 유지 |
| JWT_AUDIENCE | ProjectC.Client | 기존 토큰과 동일하게 유지 |
| JWT_LIFETIME_MINUTES | 1440, 허용 1..10080 | 선택 |
| REFRESH_LIFETIME_DAYS | 30, 허용 1..365 | 선택 |
| GUEST_PERMIT_LIMIT | 10, 연결 IP당 분당 게스트 생성 수 | 선택 |
| CATALOG_PATH | catalog/cards.json | 선택, 컨테이너 /app 기준 |
| PROJECTC_TEST_DATABASE | 전용 로컬 테스트 PostgreSQL URL | 테스트 실행 환경만 |

URL은 예를 들어 다음 형태다. 호스트·DB·role·비밀번호는 Neon 화면에서 복사한 실제 값으로 대체한다.

```dotenv
# Example only. Do not paste actual credentials into tracked files or chat.
DATABASE_URL=postgresql://ROLE:URL_ENCODED_PASSWORD@POOLED_HOST/DATABASE?sslmode=require
DIRECT_URL=postgresql://ROLE:URL_ENCODED_PASSWORD@DIRECT_HOST/DATABASE?sslmode=require
```

pooled 호스트는 일반적으로 `-pooler`를 포함한다. 두 URL의 project/branch/DB/role을 대조한다.
`sslmode=require` 및 Neon이 제공한 TLS 옵션을 유지한다. URL을 합칠 때 기존 `?` 이후에는 `&`를 사용한다.
앱은 TLS 검증을 끄는 옵션을 추가하지 않는다. 비밀번호의 예약 문자는 URL 인코딩한다.
Prisma 모델은 `@@schema`로 public/game을 지정하므로 연결 URL의 schema 하나로 테이블을 이동하지 않는다.

새 키를 만들 때는 두 번 따로 실행한다. 이미 사용 중인 JWT/Battle 키는 배포 준비만을 위해 교체하지 않는다.

```sh
openssl rand -hex 32
openssl rand -hex 32
```

로컬 npm 명령은 backend/.env를 읽으며 루트 .env는 Compose용이다.
.env 파일과 중첩 .env는 Git/Docker build context에서 제외한다.
기존 Jwt__/Battle__/Refresh__ 설정 별칭은 유지하지만 DB 접속에는 DATABASE_URL이 필요하다.

## 3. 로컬 검증

운영 DB를 테스트 변수에 넣지 않는다. 테스트는 test가 포함된 DB명과 허용한 로컬 호스트만 받는다.
아래 비밀번호는 격리 테스트용이며 운영에서 사용하지 않는다. 같은 이름의 컨테이너가 있으면 기존 것을 덮어쓰지 않는다.

```sh
# Repository root
docker run -d --name projectc-local-test-db \
  -p 127.0.0.1:55432:5432 \
  -e POSTGRES_USER=projectc_test \
  -e POSTGRES_PASSWORD=local_test_only \
  -e POSTGRES_DB=projectc_test postgres:17
docker exec projectc-local-test-db pg_isready -U projectc_test -d projectc_test

cd backend
npm ci
npm run prisma:validate
npm run prisma:generate
npm run build
npm run lint
npm test
```

다음 환경 변수는 해당 터미널에서만 로컬 DB를 가리키도록 설정한다.

```sh
# Bash
export DATABASE_URL='postgresql://projectc_test:local_test_only@localhost:55432/projectc_test?schema=public'
export DIRECT_URL="$DATABASE_URL"
export PROJECTC_TEST_DATABASE="$DATABASE_URL"
npm run test:e2e
npm run db:check
npm run prisma:status
```

```powershell
# PowerShell
$env:DATABASE_URL='postgresql://projectc_test:local_test_only@localhost:55432/projectc_test?schema=public'
$env:DIRECT_URL=$env:DATABASE_URL
$env:PROJECTC_TEST_DATABASE=$env:DATABASE_URL
npm run test:e2e
npm run db:check
npm run prisma:status
```

현재 검증: build·lint·Prisma validate/generate 성공, Jest 단위 19개 + PostgreSQL 통합 19개 통과.
기존 27개 이식 검증과 추가 호환성 검증을 유지하고 DB 점검 인증·성공·실패 검증을 추가했다.
통합 테스트에는 인증·Refresh 회전/재사용·덱 CRUD·소유권·카탈로그·매치 멱등/동시성·DB 롤백·랭크·승격전이 포함된다.
통합 테스트는 실제 migration을 적용하며 스키마를 초기화하지 않는다.
Docker 운영/migrate 이미지 실행과 전체 HTTP smoke도 통과했다. 실제 로컬 DB 중단 시
/health=200, /health/db=503, db:check exit 1 및 비밀값 비노출을 확인했고 복구 후 정상 상태를 확인했다.
연결은 되지만 테이블이 없는 로컬 DB에서는 schema=incomplete와 exit 1로 구분했다.

## 4. Neon DB 최초 스키마 생성 명령 — 수동 실행 전용

여기서 초기화는 **데이터 삭제가 아닌 최초 스키마 생성**이다. reset/db push/drop schema는 사용하지 않는다.
대상은 still-grass-68660241 / production이므로 먼저 branch·DB·role을 확인하고
기존 데이터가 있다면 백업·복원 절차와 별도 staging/Neon branch에서 검증한다.
Neon 프로젝트 생성만으로 DB가 비어 있다고 가정하지 않는다.

backend 디렉터리에서 실제 Neon DATABASE_URL/DIRECT_URL을 비밀 환경 변수로 주입한 뒤:

```sh
npm ci
npm run prisma:validate
npm run prisma:generate
npm run build
npm run db:inspect
npm run prisma:status
```

`db:inspect`는 direct URL로 READ ONLY 트랜잭션을 열고 테이블·컬럼·PK/FK/CHECK만 조회한다.
`prisma:status`는 migration 이력을 조회한다. 새 DB에서 pending migration 또는 이력 없음으로
종료 코드 1이 나올 수 있다. 그 상태를 확인하고 다음 SQL을 검토한다.

- `backend/prisma/migrations/20261102064341_add_match/migration.sql`: 기존 public.Match
- `backend/prisma/migrations/20261103110000_game_persistence/migration.sql`: game 영속 모델

**빈 DB 또는 기존 Prisma public.Match만 있는 DB**라면 승인된 작업자가 다음을 실행한다.

```sh
# This applies schema changes. Not executed against Neon by this task.
npm run prisma:migrate
npm run prisma:status
npm run db:check
```

`prisma:migrate`는 Prisma 7의 migrate deploy다. 기존 migration 파일과 checksum은 수정하지 않았다.
로컬 PostgreSQL 17에서는 두 migration을 새 DB에 적용하고 재실행 시 중복 실행 없이 최신 상태임을 확인한다.
새 migration 추가는 필요하지 않다. Neon 실제 이력은 연결 URL이 없어서 아직 확인하지 않았다.

**EF game 테이블이 이미 있는 DB**에서는 CREATE TABLE을 다시 실행하지 않는다.
[상태별 baseline 절차](docs/stack-migration.md)를 먼저 따른다. 구조가 완전히 일치함을 확인한 경우에만:

```sh
npx prisma migrate resolve --applied 20261103110000_game_persistence --config prisma.config.ts
# public.Match도 있지만 해당 Prisma 이력이 없는 경우에만 구조 확인 후:
# npx prisma migrate resolve --applied 20261102064341_add_match --config prisma.config.ts
npm run prisma:migrate
```

game.__EFMigrationsHistory는 보존한다. 구조 비교용 migrate diff가 이 테이블의 삭제 SQL을 출력해도
실행하지 않는다. 일부 테이블만 있는 DB나 schema drift는 baseline으로 숨기지 말고 개별 검토한다.

## 5. Docker 검토와 실행

저장소 루트를 context로 빌드한다. Docker build에는 DB 연결이나 비밀키가 필요 없다.

```sh
docker build --target runtime -f backend/Dockerfile -t projectc-api .
docker build --target migrate -f backend/Dockerfile -t projectc-migrate .
```

- build: npm ci → Prisma generate → NestJS build
- migrate: 별도 수동 job, `npm run prisma:migrate`
- runtime: production Node 의존성·컴파일된 JS·카탈로그·DB 점검 스크립트만 복사, 비root 사용자 node
- OpenSSL·CA 인증서를 설치해 Prisma engine과 PostgreSQL TLS를 지원
- .NET SDK/runtime·C# 소스는 운영 이미지에 포함하지 않음
- 일반 CMD는 node dist/main.js이며 DB migration을 자동 실행하지 않음
- PORT는 Render 환경 변수를 사용, 바인딩은 0.0.0.0. EXPOSE 8080은 기본값이며 Render PORT를 고정하지 않음

별도 migration 이미지 실행도 대상 DB를 검토한 작업자만 수행한다.

```sh
docker run --rm --env DATABASE_URL --env DIRECT_URL projectc-migrate
```

로컬 Compose는 별도 migrate 성공 후 NestJS를 실행한다. 기존 postgres_data 볼륨은 유지한다.
EF 테이블이 있는 기존 볼륨에는 먼저 baseline이 필요하다. production URL을 로컬 통합 테스트에 사용하지 않는다.

## 6. Render Web Service 생성 시 입력값

Render 서비스를 생성하는 행위는 배포를 시작할 수 있다. 아래 값은 준비 사항이며 실제 생성은 수동으로 수행한다.
Neon schema 준비 후에 진행한다.

| 항목 | 입력값 |
|---|---|
| Service Type | Web Service |
| Source | 현재 ProjectC-Website 저장소 |
| Name | projectc-website-api (계정 내 이름 충돌 시 변경) |
| Branch | 배포할 커밋이 있는 Git branch, 기본 main |
| Language / Runtime | Docker |
| Root Directory | 비움, 저장소 루트 |
| Dockerfile Path | ./backend/Dockerfile |
| Docker Build Context | . |
| Docker Command / Start Command | 비움, Dockerfile의 CMD 사용 |
| Build Command | 별도 입력 없음, Dockerfile 사용 |
| Pre-Deploy Command | 비움, DB migration은 위 수동 job |
| Health Check Path | /health |
| Auto Deploy | Off |
| Region | Neon production compute와 가까운 지역 선택, 실제 Neon region 확인 후 결정 |
| Instance Type | 계정의 사용 가능한 요금제에서 메모리·동시 접속 요구에 맞게 선택 |

환경 변수는 2절 표를 따른다. 앱에는 DATABASE_URL과 두 인증 키가 필수다.
DIRECT_URL은 별도 migration 환경에만 보관해도 된다. Render PORT 값을 덮어쓸 필요 없다.
`render.yaml`도 같은 설정을 제공하며 autoDeployTrigger는 off다. Blueprint 생성 역시 초기 배포를
시작할 수 있으므로 이번 작업에서는 실행하지 않았다.

API와 Battle Server의 카탈로그 버전, 서버 키, Website API HTTPS Base URL을 맞춘다.
JWT 키·issuer·audience를 변경하면 기존 사용자의 access token 호환성에 영향을 준다.

## 7. 배포 전후 점검

```sh
curl --fail https://YOUR_SERVICE.onrender.com/health
# 200 {"status":"ok"}

curl --fail https://YOUR_SERVICE.onrender.com/health/db \
  -H "X-Battle-Api-Key: $BATTLE_API_KEY"
# 200 {"status":"ok","database":"ok"}
```

/health는 기존 liveness 응답을 유지하고 DB에 접근하지 않는다. Render의 기본 점검에 사용한다.
/health/db는 서버 키를 요구하고 앱의 DATABASE_URL에 읽기 전용 SELECT 1을 실행한다.
일반 JWT만으로는 접근할 수 없다. DB 장애·타임아웃은 상세 오류 없이
503 `{"status":"unavailable","database":"unavailable"}`다. 키 누락/불일치는 401이다.
Neon compute가 잠들어 있으면 첫 연결 지연이 있을 수 있으므로 수동 진단은 간격을 두고 재시도한다.
연결 수립 제한은 10초이며 진단 트랜잭션은 별도 제한 시간을 갖는다.

SELECT 1만으로 migration이나 모든 테이블 권한을 검증하지는 않는다.
앱과 동일한 Prisma adapter·DATABASE_URL로 버전과 필수 테이블 존재를 확인하려면:

```sh
# backend/, build와 generate 이후. JWT/Battle 키는 필요 없음.
npm run db:check
# {"database":"ok","postgresqlMajor":17,"schema":"ok","missingTables":[]}

# 운영 이미지의 로컬/배포 컨테이너에서도 실행 가능:
docker exec YOUR_CONTAINER node scripts/check-db.mjs
```

db:check는 READ ONLY로 실행하고 접속 실패·테이블 누락·PostgreSQL major 불일치 시 exit 1이다.
값·URL·비밀번호를 출력하지 않는다. DIRECT_URL이 설정되어 있어도 앱 연결인 DATABASE_URL을 검사한다.
버전·테이블 존재 점검과 별도로 Prisma CLI의 direct URL에서 `npm run prisma:status`로 이력을 확인한다.
필요한 테이블 CRUD 권한과 Battle 트랜잭션 동작은 staging에서 통합 테스트/연동 점검한다.

Docker smoke script는 로컬 테스트 API에만 쓰기를 수행한다.

```sh
# 로컬 테스트 DB를 사용하는 API를 127.0.0.1:18080에 실행한 뒤:
API_BASE_URL=http://127.0.0.1:18080 BATTLE_API_KEY='<test-only-key>' node backend/scripts/docker-smoke.mjs
```

운영 URL에 smoke script를 실행하지 않는다. 실제 배포 후에는 위 읽기 전용 점검을 우선 사용한다.

## 8. 남은 작업과 제한

- 계약상 인증·덱·매치·랭크 기능의 미구현 엔드포인트는 없다.
- 실제 Neon production URL/role·DB명·region·현재 테이블·migration 이력 확인은 남아 있다.
  프로젝트 ID와 브랜치 정보만으로 실제 연결 성공을 검증했다고 볼 수 없다.
- Neon production 스키마 적용과 Render 서비스 생성·배포는 수동 작업이다.
- 별도 C++ 저장소에 HTTP 호출·결과 재전송 큐를 연결하고 실제 연동을 확인해야 한다.
- 외부 계정 연결/복구, 즉시 access token 폐기, 오래된 세션·스냅샷 정리는 기존과 같이 미구현이다.
  Refresh Token이 유실·만료되고 복구 수단이 없으면 본인 확인 복구는 불가능하다.
- 요청 제한은 현재 연결 IP 기준이다. Render 프록시 뒤에서 여러 사용자가 제한을 공유할 수 있다.
  proxy 신뢰 범위와 사용자별 제한 정책은 실제 배포 구조에 맞춰 별도 검토해야 한다.
- 개발용 Jest/CLI 전이 의존성의 audit 경고는 별도 업데이트 검토 대상이며 운영 배포와 혼동하지 않는다.

참고: [Neon Prisma 연결 가이드](https://github.com/neondatabase/website/blob/main/content/docs/guides/prisma.md),
[Render Web Service](https://render.com/docs/web-services), [Render Health Checks](https://render.com/docs/health-checks),
[node-postgres TLS](https://node-postgres.com/features/ssl).
