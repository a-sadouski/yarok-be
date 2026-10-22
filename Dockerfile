FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Yarok.Api/Yarok.Api.csproj src/Yarok.Api/
RUN dotnet restore src/Yarok.Api
COPY src/ src/
RUN dotnet publish src/Yarok.Api -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Yarok.Api.dll"]
