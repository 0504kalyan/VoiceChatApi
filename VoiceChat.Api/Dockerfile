# Build context = this directory (Api/VoiceChat.Api). See render.yaml (monorepo) and DEPLOY-STANDALONE-REPO.txt (GitHub VoiceChatApi repo).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["VoiceChat.Api.csproj", "./"]
RUN dotnet restore "VoiceChat.Api.csproj"
COPY . .
RUN dotnet publish "VoiceChat.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 10000
CMD sh -c "exec dotnet VoiceChat.Api.dll --urls http://0.0.0.0:${PORT}"
