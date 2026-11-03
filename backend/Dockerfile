FROM node:24-bookworm-slim AS base
# Prisma migration engine and PostgreSQL TLS need OpenSSL and trusted CAs.
RUN apt-get update && apt-get install -y --no-install-recommends openssl ca-certificates && rm -rf /var/lib/apt/lists/*

FROM base AS build
WORKDIR /app
COPY backend/package.json backend/package-lock.json ./
RUN npm ci
COPY backend/ ./
RUN npm run prisma:generate && npm run build

# Explicit migration job; never invoked by the normal runtime entrypoint.
FROM build AS migrate
CMD ["npm", "run", "prisma:migrate"]

FROM base AS production-dependencies
WORKDIR /app
COPY backend/package.json backend/package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force

FROM base AS runtime
ENV NODE_ENV=production
ENV PORT=8080
WORKDIR /app
COPY --from=production-dependencies /app/node_modules ./node_modules
COPY --from=build /app/package.json ./package.json
COPY --from=build /app/dist ./dist
COPY --from=build /app/catalog ./catalog
COPY --from=build /app/scripts/check-db.mjs ./scripts/check-db.mjs
USER node
EXPOSE 8080
CMD ["node", "dist/main.js"]
