# ProjectC Website API

Unity 클라이언트와 향후 C++ Battle Server가 사용할 영속 데이터 API입니다.
ASP.NET Core 10, EF Core 10, PostgreSQL 17, JWT Bearer 인증을 사용합니다.

## 기존 저장소 조사와 구조

기존 `backend/`는 NestJS 12 + Prisma 7 초기 서버로 `GET /api/matches`와 단순 Match 테이블만 있습니다.
계정·인증·덱 기능과 카드 카탈로그는 없었습니다. 기존 코드는 보존했으며 새 API는 `api/`에서 실행합니다.
PostgreSQL 서비스와 기존 볼륨을 재사용하고 새 테이블과 EF 이력은 `game` 스키마에 분리합니다.
기존 Prisma `public` 테이블은 변경하거나 자동 이관하지 않습니다. 기존 매치에는 덱/종료 사유 등의 정보가 없어 별도 이관 정책이 필요합니다.

```text
api/
  ProjectC.Api/
    Auth/                 JWT 발급과 인증 설정
    Catalog/cards.json    원본 CardId·메인 덱 허용 여부·규칙·버전
    Data/                 DbContext, CLI factory, EF migrations
    Decks/                교체 가능한 ICardCatalog와 덱 검증
    Domain/               사용자·덱·랭크·매치 모델
    Endpoints/            API와 오류 처리
    Dockerfile
  ProjectC.Api.Tests/     실제 PostgreSQL을 사용하는 HTTP/DB 통합 테스트
backend/                  기존 NestJS 서버 (별도 보존)
scripts/export-card-catalog.py
ProjectC.sln
docker-compose.yml        DB + 일회성 migration + API
```

## Docker로 로컬 실행

Docker Engine과 Compose v2가 필요합니다. 아래 명령은 저장소 루트에서 실행합니다.

1. `.env.example`을 `.env`로 복사합니다. 기존 `.env`가 있다면 보존하고 필요한 항목을 추가합니다.
2. `openssl rand -hex 32`를 두 번 실행하여 서로 다른 값을 `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY`에 넣습니다.
   DB 비밀번호에 연결 문자열 구분자인 `;`를 넣지 않도록 hex 값을 권장합니다.
3. 실행합니다.

```bash
docker compose up --build -d
curl http://localhost:8080/health
docker compose logs api migrate
```

DB가 준비되면 `migrate`가 EF 마이그레이션을 적용하고 성공한 뒤 API가 시작됩니다.
API와 DB 포트는 로컬호스트에 바인딩됩니다. 종료는 `docker compose down`이며 볼륨은 유지됩니다.
**기존 볼륨이 있다면 초기화 당시의 DB 이름·사용자·비밀번호를 `.env`에 지정해야 합니다.**
PostgreSQL 이미지의 초기화 환경 변수는 기존 볼륨의 계정 비밀번호를 변경하지 않습니다.

소스 변경 후에는 `docker compose up --build -d`로 다시 빌드합니다.
이미 적용된 마이그레이션의 재실행은 EF 이력으로 건너뜁니다.
DB만 띄우려면 `docker compose up -d postgres`를 사용합니다.

## .NET SDK로 개발

.NET 10 SDK를 설치합니다. DB는 위 Compose의 postgres 서비스를 사용할 수 있습니다.
ASP.NET Core는 루트 `.env`를 자동으로 읽지 않습니다. 아래 값을 로컬 실제 값으로 설정합니다.

```bash
dotnet tool restore
dotnet restore ProjectC.sln
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__GameDatabase='Host=localhost;Port=5432;Database=projectc_analytics;Username=projectc;Password=<로컬 DB 비밀번호>'
export Jwt__SigningKey='<생성한 64자리 hex 키>'
dotnet ef database update --project api/ProjectC.Api
dotnet run --project api/ProjectC.Api --urls http://localhost:8080
```

JWT 키는 환경 변수 대신 `dotnet user-secrets set 'Jwt:SigningKey' '<키>' --project api/ProjectC.Api`로도 지정할 수 있습니다.
마이그레이션 CLI factory는 연결 문자열을 환경 변수 `ConnectionStrings__GameDatabase`에서 읽습니다.
Dev Container를 사용하면 `.env` 준비 후 열고, 컨테이너 터미널에서 `dotnet ef database update --project api/ProjectC.Api`,
`dotnet run --project api/ProjectC.Api`를 실행합니다. 기존 Node Dev Container는 .NET SDK 환경으로 전환했습니다.

## DB 마이그레이션

초기 migration과 model snapshot이 포함되어 있습니다. `EnsureCreated`를 사용하지 않습니다.
위 환경 변수와 도구 복원을 준비한 후:

```bash
# 모델 변경 시 새 migration 생성
dotnet ef migrations add ChangeDescription --project api/ProjectC.Api --output-dir Data/Migrations
# 적용
dotnet ef database update --project api/ProjectC.Api
# 운영 검토용 SQL 생성
dotnet ef migrations script --idempotent --project api/ProjectC.Api --output migration.sql
```

컨테이너 이미지로 명시적으로 적용하려면 `docker compose run --rm migrate`를 사용합니다.
앱 자체는 시작 시 DB 스키마를 변경하지 않으며 `--migrate` 모드에서만 적용 후 종료합니다.
운영에서는 승인된 migration을 단일 작업에서 적용한 후 API를 배포합니다.

## API 계약

`POST /api/auth/guest`만 공개됩니다. 나머지는 `Authorization: Bearer <accessToken>` 헤더가 필요합니다.
JSON에 알 수 없는 필드를 보내면 400으로 거절합니다. PlayerId는 토큰의 검증된 `sub`에서만 가져옵니다.

| API | 요청/응답 |
| --- | --- |
| POST /api/auth/guest | `{ "displayName": "Guest" }` → accessToken, tokenType, expiresAt, user |
| GET /api/users/me | playerId(UUID), displayName, createdAt(UTC) |
| GET /api/decks | 현재 사용자 덱 배열 |
| POST /api/decks | name, cards → 201과 저장한 덱 |
| PUT /api/decks/{id} | name, cards 전체 교체 → 저장한 덱 |
| DELETE /api/decks/{id} | 204 |
| GET /api/rank/me | mmr, tier, division, rp, winStreak, promotionStatus, promotionGames |

게스트 생성 요청에 표시 이름을 생략하면 `Guest`를 사용합니다(본문 `{}` 필요).
표시 이름은 공백 정리 후 1~32자, 덱 이름은 1~64자입니다. UUID는 서버에서 생성합니다.
토큰은 기본 24시간 유효하며 서명·알고리즘·발급자·대상·만료·실제 계정 존재를 검증합니다.
이번 단계에는 갱신/게스트 복구 기능이 없습니다. 토큰 소실·만료 시 기존 계정으로 다시 로그인할 수 없으므로
장기 계정 운영 전 계정 연결 또는 복구/갱신 설계가 필요합니다. 게스트 생성은 IP당 분당 10회로 제한합니다(429).

덱 요청 예시(정확히 30장, 각 3장):

```json
{
  "name": "백귀야행",
  "cards": [
    { "cardId": 1001, "quantity": 3 },
    { "cardId": 1002, "quantity": 3 },
    { "cardId": 1003, "quantity": 3 },
    { "cardId": 1004, "quantity": 3 },
    { "cardId": 1005, "quantity": 3 },
    { "cardId": 1006, "quantity": 3 },
    { "cardId": 1008, "quantity": 3 },
    { "cardId": 1101, "quantity": 3 },
    { "cardId": 2001, "quantity": 3 },
    { "cardId": 2002, "quantity": 3 }
  ]
}
```

미등록 ID, 메인 덱 금지 카드(예: 1007), 총 매수 불일치, 1~3 범위 밖 수량, 동일 ID를 여러 항목으로 전송하면 400 ValidationProblem입니다.
다른 사용자의 덱이나 없는 덱 수정·삭제는 모두 404로 처리합니다. 덱 생성/수정 요청은 PlayerId를 받지 않습니다.
동시 삭제/수정 충돌은 409이며 재조회 후 재시도합니다. 인증 누락·유효하지 않은 토큰은 401입니다.

```bash
curl -X POST http://localhost:8080/api/auth/guest \
  -H 'Content-Type: application/json' -d '{"displayName":"테스터"}'
curl http://localhost:8080/api/users/me -H 'Authorization: Bearer <accessToken>'
```

## 카드 카탈로그 동기화

[ProjectC 원본](https://github.com/dawninthemoon/ProjectC/tree/06fc300ef4275b4acdb3ca440b3adf121c5aa765/ProjectC.Server/Data/HyakkiYagyo)의
커밋 `06fc300ef4275b4acdb3ca440b3adf121c5aa765`를 기준으로 합니다.
`Cards.json`에서 ID/허용 여부, `Decks.json`에서 30장/최대 3장 규칙만 추출했습니다.
원본 `CardCatalog.hpp`와 Unity `PersonalDeck.cs`의 개인 덱 규칙도 동일합니다.
효과·스탯·텍스트·이미지는 DB에도 내보내기 파일에도 복제하지 않습니다.

```bash
python3 scripts/export-card-catalog.py /path/to/ProjectC --ref <검증한 커밋>
```

변경된 `Catalog/cards.json`을 검토하고 테스트한 후 API를 재배포합니다.
각 덱에는 검증 당시 `catalogVersion`이 저장됩니다. 배틀 시작 시 최신 카탈로그로 다시 검증하는 작업은
향후 Battle Server 연동 단계에서 수행해야 합니다. 다른 카탈로그 공급자는 `ICardCatalog`로 교체할 수 있습니다.

## 랭크와 매치 저장 설계

가입 시 랭크 행을 함께 생성합니다. 기본 MMR 1000, Tier `Unranked`, Division/RP/연승 0,
PromotionStatus `None`은 초기 저장 값이며 랭크 계산 알고리즘은 없습니다.
승격전 기록은 사용자·SeriesId·GameNumber 키와 Won 값으로 시리즈별 승패를 보존합니다.

매치는 MatchId(UUID) PK로 재전송에 의한 중복 저장을 DB에서 차단합니다.
참가자와 당시 덱 ID/이름/카탈로그 버전/카드 수량 스냅샷을 별도 저장하므로 덱 수정·삭제 뒤에도 이력이 유지됩니다.
승자가 없는 종료는 WinnerPlayerId=null로 표현하며, 승자는 참가자에 대한 복합 FK로 제한합니다.
향후 writer에서는 트랜잭션 안에서 매치와 참가자를 먼저 저장하고 승자 FK를 설정한 후 커밋합니다.
참가자 수·종료 사유 enum·승패 일관성 등은 향후 인증된 서버 입력 계층에서 검증해야 합니다.
동일 MatchId 재전송은 향후 서버 전용 API에서 기존 결과와 비교 후 성공/충돌로 처리할 예정이며,
현재 구현 범위는 고유 키와 저장 모델까지입니다.

실시간 진행과 승패 판정은 Battle Server의 책임입니다. Website API에는 클라이언트가 매치 결과나 랭크를 쓰는 경로가 없습니다.
C++ 서버 인증과 결과 수신은 별도의 서버 권한·서비스 인증을 추가할 확장 지점이며 기존 서버 프로토콜은 변경하지 않았습니다.

## 설정과 운영

공통 설정은 `appsettings.json`, 개발 로깅은 `appsettings.Development.json`, 운영 로깅은 `appsettings.Production.json`입니다.
DB 연결 문자열과 JWT 키는 설정 파일에 포함하지 않습니다. `.env`, 빌드 산출물, 로컬 설정 파일은 Git/Docker 빌드에서 제외됩니다.
운영에는 `ASPNETCORE_ENVIRONMENT=Production`, `ConnectionStrings__GameDatabase`, `Jwt__SigningKey`를
배포 환경의 비밀 저장소로 주입합니다. API 앞단에서 HTTPS를 종료하도록 구성하고,
프록시 IP 전달·생성 제한 정책·허용 호스트는 실제 배포 구조에 맞게 설정합니다.

## 테스트

테스트는 **전용 DB의 game 스키마를 초기화**하고 실제 EF migration을 적용합니다. 운영/개발 DB를 지정하지 마세요.

```bash
export PROJECTC_TEST_DATABASE='Host=localhost;Database=projectc_test;Username=<테스트 사용자>;Password=<테스트 비밀번호>'
dotnet test ProjectC.sln
```

게스트/랭크 영속화, 서명·발급자·대상·만료 검증, 타 계정 접근 거절, 덱 CRUD·검증,
매치 중복 키와 스냅샷 보존, migration/model 일치 여부를 검사합니다.
GitHub Actions `api-ci.yml`은 PostgreSQL 17과 .NET 10으로 빌드·테스트·publish합니다.
기존 NestJS CI는 별도로 유지됩니다.

참고: [ASP.NET Core JWT 검증](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0),
[EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/).
