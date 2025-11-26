# =============================
# STAGE 1: BUILD
# =============================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy file csproj vào container
COPY TMD/TMD/TMD.csproj ./TMD.csproj

# Restore dependencies
RUN dotnet restore "./TMD.csproj"

# Copy toàn bộ source code
COPY TMD/. ./TMD/

# Build & publish
WORKDIR /src/TMD
RUN dotnet publish -c Release -o /app/publish

# =============================
# STAGE 2: RUNTIME
# =============================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "TMD.dll"]
