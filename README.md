# SmartInventory Project

Этот документ содержит руководства по развертыванию локального окружения, настройке секретов для .NET приложения и запуску базы данных Microsoft SQL Server с использованием Docker Compose.

---

## 🛠️ Требования

Перед началом убедитесь, что у вас установлены:
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Git](https://git-scm.com/)

Проверка:
```bash
dotnet --list-sdks
docker --version
git --version
```

---

## 🚀 Быстрый старт

### 1. Клонирование репозитория
```bash
git clone <URL_ВАШЕГО_РЕПОЗИТОРИЯ>
cd <ИМЯ_ПАПКИ_ПРОЕКТА>
```

---

## 🐳 2. Настройка Docker и запуск базы данных

Для хранения паролей и конфигурации Docker используется файл локальных переменных `.env`.

#### Шаг 2.1. Создание файла `.env`
Скопируйте пример файла конфигурации `.env.example` и создайте на его основе локальный файл `.env`:

_Для Linux / macOS:_
```bash
cp .env.example .env
```
_Для Windows (PowerShell):_
```bash
Copy-Item .env.example .env
```

#### Шаг 2.2. Заполнение секретов в `.env`
Откройте созданный файл `.env` и задайте надежный пароль для учетной записи `sa`:

```env
MSSQL_SA_PASSWORD=passwd123
```

#### Шаг 2.3. Запуск контейнера SQL Server
Запустите базу данных в фоновом режиме через Docker Compose:

```bash
docker compose up -d
```

Проверить статус запущенного контейнера можно командой:
```bash
docker ps
```

---

## 🔑 3. Настройка локальных секретов (.NET User Secrets)

#### Шаг 3.1. Инициализация User Secrets
Перейдите в папку с проектом (где находится файл `.csproj`) и выполните инициализацию:

```bash
dotnet user-secrets init
```

#### Шаг 3.2. Установка строки подключения
Установите строку подключения к локальной базе данных SQL Server (пароль должен совпадать с указанным в `.env`):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=SmartInventoryDb;User Id=sa;Password=passwd123;TrustServerCertificate=True;"
```

#### Просмотр сохраненных секретов
Вы всегда можете проверить текущие локальные секреты командой:
```bash
dotnet user-secrets list
```

---

## 🗄️ 4. Применение миграций базы данных

После того как база данных запущенa в Docker, а секреты настроены, примените миграции Entity Framework Core для создания структур таблиц:

```bash
dotnet ef database update
```

_Если утилита `dotnet ef` не установлена, установите её командой:_
```bash
dotnet tool install --global dotnet-ef
```

---

## 🎯 5. Запуск приложения

Запустите проект в режиме разработки:

```bash
dotnet run
```

Приложение автоматически подтянет настройки из `appsettings.json` и переопределит строку подключения значениями из вашего локального `User Secrets`.

## 🚫 Закрытие docker
```bash
docker compose down
```