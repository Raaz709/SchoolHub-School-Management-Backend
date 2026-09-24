FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["SchoolHub.API.csproj", "./"]
RUN dotnet restore "SchoolHub.API.csproj"
COPY . .
RUN dotnet publish "SchoolHub.API.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM base AS publish
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SchoolHub.API.dll"]
