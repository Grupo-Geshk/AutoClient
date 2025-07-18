# Imagen base oficial de .NET 8 SDK para build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /app

# Copiar el csproj y restaurar dependencias
COPY ["AutoClient/AutoClient.csproj", "AutoClient/"]
RUN dotnet restore "AutoClient/AutoClient.csproj"

# Copiar el resto del código
COPY . .

WORKDIR /app/AutoClient
RUN dotnet publish "AutoClient.csproj" -c Release -o /out

# Imagen final: runtime (más liviana)
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY --from=build /out .

# Exponer puerto 8080
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AutoClient.dll"]
