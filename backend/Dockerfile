FROM node:24-bookworm-slim AS build
RUN apt-get update && apt-get install -y --no-install-recommends openssl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY backend/package.json backend/package-lock.json ./
RUN npm ci
COPY backend/ ./
RUN npm run prisma:generate && npm run build

# Explicit migration job; never invoked by the normal runtime entrypoint.
FROM build AS migrate
CMD ["npm", "run", "prisma:migrate"]

FROM node:24-bookworm-slim AS production-dependencies
WORKDIR /app
COPY backend/package.json backend/package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force

FROM node:24-bookworm-slim AS runtime
ENV NODE_ENV=production
ENV PORT=8080
WORKDIR /app
COPY --from=production-dependencies /app/node_modules ./node_modules
COPY --from=build /app/package.json ./package.json
COPY --from=build /app/dist ./dist
COPY --from=build /app/catalog ./catalog
USER node
EXPOSE 8080
CMD ["node", "dist/main.js"]
