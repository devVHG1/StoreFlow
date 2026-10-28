FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["StoreFlow.Domain/StoreFlow.Domain.csproj", "StoreFlow.Domain/"]
COPY ["StoreFlow.Application/StoreFlow.Application.csproj", "StoreFlow.Application/"]
COPY ["StoreFlow.Infrastructure/StoreFlow.Infrastructure.csproj", "StoreFlow.Infrastructure/"]
COPY ["StoreFlow.Аpi/StoreFlow.Аpi.csproj", "StoreFlow.Аpi/"]
RUN dotnet restore "StoreFlow.Аpi/StoreFlow.Аpi.csproj"
COPY . .
RUN dotnet publish "StoreFlow.Аpi/StoreFlow.Аpi.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "StoreFlow.Аpi.dll"]
