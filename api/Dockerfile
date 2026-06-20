# syntax=docker/dockerfile:1

# --- Build the .NET isolated worker and publish the Functions app ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY api.csproj ./
RUN dotnet restore api.csproj
COPY . .
RUN dotnet publish api.csproj -c Release -o /home/site/wwwroot

# --- Azure Functions runtime image (dotnet-isolated, host listens on :80) ---
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated9.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
COPY --from=build /home/site/wwwroot /home/site/wwwroot
