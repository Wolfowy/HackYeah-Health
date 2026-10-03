FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY DocPrep.sln ./
COPY src/DocPrep.Domain/DocPrep.Domain.csproj src/DocPrep.Domain/
COPY src/DocPrep.Application/DocPrep.Application.csproj src/DocPrep.Application/
COPY src/DocPrep.Infrastructure/DocPrep.Infrastructure.csproj src/DocPrep.Infrastructure/
COPY src/DocPrep.Api/DocPrep.Api.csproj src/DocPrep.Api/
COPY tests/DocPrep.UnitTests/DocPrep.UnitTests.csproj tests/DocPrep.UnitTests/
RUN dotnet restore DocPrep.sln
COPY . .
RUN dotnet publish src/DocPrep.Api/DocPrep.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "DocPrep.Api.dll"]
