# Build and publish the app
FROM mcr.microsoft.com/dotnet/core/sdk:2.2 AS build-env
WORKDIR /app

# Copy project files and restore as distinct layers
COPY src/KongoloGym/*.fsproj ./
RUN dotnet restore

# Copy everything else and build
COPY src/KongoloGym ./
RUN dotnet publish -c Release -o out

#Build runtime docker image
FROM mcr.microsoft.com/dotnet/core/aspnet:2.2
WORKDIR /app
COPY --from=build-env /app/out .

EXPOSE 80
EXPOSE 443

ENTRYPOINT [ "dotnet", "KongoloGym.dll" ]