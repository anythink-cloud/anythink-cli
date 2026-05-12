FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files and restore
COPY ["mcp/AnythinkMcp.csproj", "mcp/"]
COPY ["src/AnythinkCli.csproj", "src/"]
RUN dotnet restore "mcp/AnythinkMcp.csproj"

# Copy all source and publish
COPY . .
RUN dotnet publish "mcp/AnythinkMcp.csproj" -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=0
ENV DOTNET_TieredCompilation=1
ENV DOTNET_ReadyToRun=1
ENV DOTNET_GCConserveMemory=9

EXPOSE 5300
ENTRYPOINT ["dotnet", "anythink-mcp.dll", "--http", "--port", "5300"]
