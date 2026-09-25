FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Nova.Server/Nova.Server.csproj", "src/Nova.Server/"]
RUN dotnet restore "src/Nova.Server/Nova.Server.csproj"

COPY . .
WORKDIR "/src/src/Nova.Server"
RUN dotnet publish "Nova.Server.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .

# The official .NET 10 runtime image provides a non-root "app" user.
USER app

ENTRYPOINT ["dotnet", "Nova.Server.dll"]
