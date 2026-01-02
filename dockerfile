# Etapa 1: Compilación (Build)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar csproj y restaurar dependencias
COPY ["MyDiscordBot.csproj", "./"]
RUN dotnet restore "MyDiscordBot.csproj"

# Copiar el resto del código y publicar
COPY . .
RUN dotnet publish "MyDiscordBot.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Etapa 2: Ejecución (Runtime)
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

# Crear carpeta interna 'storage' para la base de datos persistente.
# Esto separa los datos de la aplicación compilada.
RUN mkdir -p /app/storage

COPY --from=build /app/publish .

# Comando de entrada
ENTRYPOINT ["dotnet", "MyDiscordBot.dll"]