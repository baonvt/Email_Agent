# syntax=docker/dockerfile:1
# Visual Studio fast mode runs this stage; the published image uses final.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER $APP_UID
EXPOSE 8080
EXPOSE 8081


FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "./"]
COPY ["Project_AI/Project_AI.API.csproj", "Project_AI/packages.lock.json", "Project_AI/"]
COPY ["Project_AI.Application/Project_AI.Application.csproj", "Project_AI.Application/packages.lock.json", "Project_AI.Application/"]
COPY ["Project_AI.Domain/Project_AI.Domain.csproj", "Project_AI.Domain/packages.lock.json", "Project_AI.Domain/"]
COPY ["Project_AI.Infrastructure/Project_AI.Infrastructure.csproj", "Project_AI.Infrastructure/packages.lock.json", "Project_AI.Infrastructure/"]
RUN --mount=type=cache,id=inboxagent-nuget,target=/root/.nuget/packages \
    dotnet restore "Project_AI/Project_AI.API.csproj" --locked-mode
COPY ["Project_AI/", "Project_AI/"]
COPY ["Project_AI.Application/", "Project_AI.Application/"]
COPY ["Project_AI.Domain/", "Project_AI.Domain/"]
COPY ["Project_AI.Infrastructure/", "Project_AI.Infrastructure/"]

# Publish builds once; a separate dotnet build would repeat the compilation.
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,id=inboxagent-nuget,target=/root/.nuget/packages \
    dotnet publish "Project_AI/Project_AI.API.csproj" -c $BUILD_CONFIGURATION \
    -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
COPY --from=publish --chown=$APP_UID:$APP_UID /app/publish .
ENTRYPOINT ["dotnet", "Project_AI.API.dll"]
