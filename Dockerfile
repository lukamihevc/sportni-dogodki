
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SportniDogodki.csproj", "./"]
RUN dotnet restore "SportniDogodki.csproj"
COPY . .
RUN dotnet publish "SportniDogodki.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SportniDogodki.dll"]
