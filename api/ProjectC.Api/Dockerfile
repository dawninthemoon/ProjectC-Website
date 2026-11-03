FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY api/ProjectC.Api/ProjectC.Api.csproj api/ProjectC.Api/
RUN dotnet restore api/ProjectC.Api/ProjectC.Api.csproj
COPY api/ProjectC.Api/ api/ProjectC.Api/
RUN dotnet publish api/ProjectC.Api/ProjectC.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProjectC.Api.dll"]
