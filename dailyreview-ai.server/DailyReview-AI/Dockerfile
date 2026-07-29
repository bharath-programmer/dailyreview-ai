# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies before copying source to maximize layer-cache reuse.
COPY ["DailyReview-AI/DailyReview-AI.csproj", "DailyReview-AI/"]
COPY ["DailyReview.Core/DailyReview.Core.csproj", "DailyReview.Core/"]
RUN dotnet restore "DailyReview-AI/DailyReview-AI.csproj"

COPY . .
RUN dotnet publish "DailyReview-AI/DailyReview-AI.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# Chiseled Extra keeps the ASP.NET runtime image small while retaining globalization support.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app

COPY --from=build /app/publish ./

USER $APP_UID
ENTRYPOINT ["dotnet", "DailyReview-AI.dll"]
