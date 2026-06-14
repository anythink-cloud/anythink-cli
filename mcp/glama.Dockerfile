# Dockerfile for the stdio Anythink MCP server — used for Glama's introspection
# check (and any container that wants to run the server over stdio).
#
# Ships BOTH binaries: `anythink-mcp` (the MCP server) and `anythink` (the CLI).
# In stdio mode the MCP's generic `cli` tool shells out to `anythink` on PATH,
# so both are required for a fully working server — not just `anythink-mcp`.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["mcp/AnythinkMcp.csproj", "mcp/"]
COPY ["src/AnythinkCli.csproj", "src/"]
RUN dotnet restore "mcp/AnythinkMcp.csproj"

COPY . .
RUN dotnet publish "src/AnythinkCli.csproj" -c Release -o /app/cli \
 && dotnet publish "mcp/AnythinkMcp.csproj" -c Release -o /app/mcp

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app
COPY --from=build /app/cli ./cli
COPY --from=build /app/mcp ./mcp

# Put `anythink` on PATH so the stdio `cli` tool can invoke it.
RUN printf '#!/bin/sh\nexec dotnet /app/cli/anythink.dll "$@"\n' > /usr/local/bin/anythink \
 && chmod +x /usr/local/bin/anythink

ENV NO_COLOR=1
# stdio mode is the default (no --http) — this is what Glama introspects.
ENTRYPOINT ["dotnet", "/app/mcp/anythink-mcp.dll"]
