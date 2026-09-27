# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy csproj files and restore dependencies to take advantage of docker caching
COPY src/BSStore.Domain/BSStore.Domain.csproj src/BSStore.Domain/
COPY src/BSStore.Application/BSStore.Application.csproj src/BSStore.Application/
COPY src/BSStore.Infrastructure/BSStore.Infrastructure.csproj src/BSStore.Infrastructure/
COPY src/BSStore.API/BSStore.API.csproj src/BSStore.API/

RUN dotnet restore src/BSStore.API/BSStore.API.csproj

# Copy source code
COPY src/ src/

# Publish Release
RUN dotnet publish src/BSStore.API/BSStore.API.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Ensure uploads directory exists
RUN mkdir -p /app/wwwroot/uploads

COPY --from=build /app/publish .

# Default environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=5295
EXPOSE 5295

ENTRYPOINT ["dotnet", "BSStore.API.dll"]
