# ProjectC NestJS API

NestJS 12, TypeScript, Prisma ORM 7.10.1, PostgreSQL 17, Jest 30, Node 24를 사용합니다.
운영 HTTP 계약은 [Battle Server API](../docs/battle-server-api.md), 기존 EF DB 인수와
차이는 [스택 전환 문서](../docs/stack-migration.md)에 있습니다.

## 개발

backend 디렉터리에서 실행합니다. .env.example을 .env로 복사하고 실제 연결·비밀값을 주입합니다.
루트 Compose는 루트 .env를 사용하며 npm/dotenv는 backend/.env를 사용합니다.

```sh
npm ci
npm run prisma:generate
npm run prisma:validate
npm run db:inspect
# DB 상태를 확인하고 migration/baseline 절차를 검토한 뒤:
npm run prisma:migrate
npm run start:dev
```

Prisma CLI는 DIRECT_URL 우선, 앱은 DATABASE_URL을 사용합니다.
API 기본 PORT는 8080이며 0.0.0.0에 바인딩합니다.

## 검증

```sh
npm run build
npm run lint
npm test
# 테스트 전용 로컬 DB만 지정합니다. PostgreSQL URL 형식입니다.
export PROJECTC_TEST_DATABASE='postgresql://projectc_test:<password>@localhost:55432/projectc_test?schema=public'
npm run test:e2e
```

Jest는 TypeScript를 .test-dist로 컴파일한 뒤 ESM VM modules로 실행합니다.
현재 Node에서는 VM modules 실험 경고가 표시될 수 있습니다.
통합 테스트는 실제 migration을 적용하고 데이터·스키마를 자동 초기화하지 않습니다.
production 또는 이름에 test가 없는 DB는 거부합니다.
반복 실행해도 테스트가 UUID로 격리되며 전용 DB만 사용해야 합니다.

기존 27개 C# 검증 항목을 모두 이식했고 기존 public.Match 조회 단위 테스트도 Jest로 유지합니다.
추가로 .NET 직렬화 해시 벡터, 기존 PascalCase 내부 JSON, 승격전 실패 영속화와 FK를 검사합니다.

## Docker / Render

저장소 루트에서:

```sh
docker build --target runtime -f backend/Dockerfile -t projectc-api .
```

운영 이미지는 Node, NestJS, Prisma JS Client를 사용하며 .NET/EF를 포함하지 않습니다.
시작 명령은 node dist/main.js이며 자동 migration은 없습니다.
별도 migrate target과 Render 설정은 스택 전환 문서를 따릅니다.
