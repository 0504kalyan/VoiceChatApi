# Monorepo Dockerfile: build context is this Api folder (see paths below).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["VoiceChat.Api/VoiceChat.Api.csproj", "VoiceChat.Api/"]
WORKDIR /src/VoiceChat.Api
RUN dotnet restore "VoiceChat.Api.csproj"
COPY VoiceChat.Api/ .
RUN dotnet publish "VoiceChat.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# Linux containers: force Production so appsettings.Development.json is not merged into the published image.
ENV ASPNETCORE_ENVIRONMENT=Production
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 10000
ENV PORT=10000
CMD sh -c "exec dotnet VoiceChat.Api.dll --urls http://0.0.0.0:${PORT}"
