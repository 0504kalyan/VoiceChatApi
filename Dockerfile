# Copy this entire folder's files to the ROOT of https://github.com/0504kalyan/VoiceChatApi
# (next to VoiceChat.sln). Commit Dockerfile + render.yaml + .dockerignore, then push main.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["VoiceChat.Api/VoiceChat.Api.csproj", "VoiceChat.Api/"]
WORKDIR /src/VoiceChat.Api
RUN dotnet restore "VoiceChat.Api.csproj"
COPY VoiceChat.Api/ .
RUN dotnet publish "VoiceChat.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# Render runs Linux — never load appsettings.Development.json (local SQL Server / Trusted_Connection).
# Without this, ASPNETCORE_ENVIRONMENT can default in ways that merge Development settings and ignore cloud SQL.
ENV ASPNETCORE_ENVIRONMENT=Production
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 10000
CMD sh -c "exec dotnet VoiceChat.Api.dll --urls http://0.0.0.0:${PORT}"
