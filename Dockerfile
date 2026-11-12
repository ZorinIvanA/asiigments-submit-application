# Один контейнер = клиент + API (деплой в Amvera, лаба 6 CI/CD).
#
# Схема: node собирает Angular-клиент → sdk публикует ASP.NET Core API,
# статика клиента кладётся в wwwroot → runtime-образ, где Kestrel на 80-м
# порту отдаёт и /api/v1, и SPA (Program.cs: UseStaticFiles + SPA-fallback).
#
# Сборку запускает Amvera по этому Dockerfile из корня репозитория
# (см. amvera.yml и .github/workflows/deploy.yml).

# --- Стадия 1: сборка Angular-клиента ---------------------------------------
FROM node:22-alpine AS client
WORKDIR /app

# Сначала только манифесты — слой с node_modules кэшируется, пока
# зависимости не меняются.
COPY package.json package-lock.json ./
RUN npm ci

# Конфиги и исходники, нужные `ng build` (тесты и зоны в сборке не участвуют).
COPY angular.json tsconfig.json tsconfig.app.json ./
COPY public ./public
COPY src/client ./src/client
RUN npm run build
# Результат: /app/dist/asiigments-submit-application/browser

# --- Стадия 2: публикация ASP.NET Core API ----------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src

# Кэш слоёв restore: сначала один csproj (ProjectReference у LabsApp нет),
# затем исходники.
COPY src/api/LabsApp/LabsApp.csproj LabsApp/
RUN dotnet restore LabsApp/LabsApp.csproj

COPY src/api/LabsApp ./LabsApp
RUN dotnet publish LabsApp/LabsApp.csproj -c Release -o /app/publish

# Собранный клиент — в wwwroot публиката: API начнёт его раздавать.
COPY --from=client /app/dist/asiigments-submit-application/browser /app/publish/wwwroot

# --- Стадия 3: runtime -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Production: валидатор StorageOptions требует явную строку подключения —
# её задаём переменной Storage__ConnectionString в личном кабинете Amvera
# (внутренний хост управляемой PostgreSQL), здесь её НЕ прописываем.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:80

COPY --from=api /app/publish .
EXPOSE 80

ENTRYPOINT ["dotnet", "LabsApp.dll"]
