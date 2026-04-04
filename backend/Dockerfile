FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/ ./backend/
COPY prompts/ ./prompts/
WORKDIR /src/backend

RUN dotnet restore ./MunicipalityChatbot.sln
RUN dotnet publish ./src/MunicipalityChatbot.Api/MunicipalityChatbot.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

COPY --from=build /app/publish ./
COPY --from=build /src/prompts ./prompts/
ENTRYPOINT ["dotnet","MunicipalityChatbot.Api.dll"]

