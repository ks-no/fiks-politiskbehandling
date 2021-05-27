FROM mcr.microsoft.com/dotnet/sdk:5.0-alpine AS core-build-env
WORKDIR /build-app

# Copy csproj and build + publish code
COPY ks.fiks.io.politiskbehandling.sample  ./ks.fiks.io.politiskbehandling.sample 

RUN dotnet restore ks.fiks.io.politiskbehandling.sample/ks.fiks.io.politiskbehandling.sample.csproj
RUN dotnet build --configuration Release ks.fiks.io.politiskbehandling.sample/ks.fiks.io.politiskbehandling.sample.csproj
RUN dotnet publish --configuration Release ks.fiks.io.politiskbehandling.sample/ks.fiks.io.politiskbehandling.sample.csproj --no-build --output published-app

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:5.0
WORKDIR /app
COPY --from=core-build-env /build-app/published-app .

ENTRYPOINT ["dotnet", "ks.fiks.io.politiskbehandling.sample.dll"]
