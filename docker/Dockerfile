FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Evoque.Billing.Api/Evoque.Billing.Api.csproj Evoque.Billing.Api/
RUN dotnet restore Evoque.Billing.Api/Evoque.Billing.Api.csproj

COPY . .
RUN dotnet publish Evoque.Billing.Api/Evoque.Billing.Api.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Evoque.Billing.Api.dll"]

