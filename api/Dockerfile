# A API do dev.kit.page em contêiner (Azure Container Apps ou App Service for Containers).
# Contexto do build: a pasta api/.   docker build -t devkitpage-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/DevKitPage.Api/DevKitPage.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# A base SQLite fica num volume (/app/dados). No Azure SQL: Banco__Provider=SqlServer e ConnectionStrings__DevKitPage.
# Segredos (Jwt__Segredo, Telemetria__CodigoDeRegistro, Seed__AdminPassword) vêm do ambiente / Key Vault — nunca da imagem.
VOLUME /app/dados
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DevKitPage.Api.dll"]
