# Build the ASP.NET Core API from the repository root.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["HoyDonde.API/HoyDonde.API.csproj", "HoyDonde.API/"]
RUN dotnet restore "HoyDonde.API/HoyDonde.API.csproj"

COPY HoyDonde.API/ HoyDonde.API/
WORKDIR /src/HoyDonde.API
RUN dotnet publish "HoyDonde.API.csproj" --configuration Release --output /app/publish --no-restore

# Cloud Run receives requests on port 8080. The application runs as the non-root user included
# in the .NET 8 ASP.NET runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080

COPY --from=build --chown=app:app /app/publish ./
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "HoyDonde.API.dll"]
