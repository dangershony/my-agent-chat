FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/MyAgentChat/ ./MyAgentChat/
RUN dotnet publish MyAgentChat/MyAgentChat.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MyAgentChat.dll"]
