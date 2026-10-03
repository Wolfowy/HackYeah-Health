FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY HealthPrep.sln ./
COPY src/HealthPrep.Domain/HealthPrep.Domain.csproj src/HealthPrep.Domain/
COPY src/HealthPrep.Application/HealthPrep.Application.csproj src/HealthPrep.Application/
COPY src/HealthPrep.Infrastructure/HealthPrep.Infrastructure.csproj src/HealthPrep.Infrastructure/
COPY src/HealthPrep.Api/HealthPrep.Api.csproj src/HealthPrep.Api/
COPY tests/HealthPrep.UnitTests/HealthPrep.UnitTests.csproj tests/HealthPrep.UnitTests/
RUN dotnet restore HealthPrep.sln
COPY . .
RUN dotnet publish src/HealthPrep.Api/HealthPrep.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "HealthPrep.Api.dll"]
