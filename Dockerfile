FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ChessGame.Api.csproj ./

RUN dotnet restore ChessGame.Api.csproj

COPY . .

RUN dotnet publish ChessGame.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:10000

EXPOSE 10000

ENTRYPOINT ["dotnet", "ChessGame.Api.dll"]