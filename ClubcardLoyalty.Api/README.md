# Clubcard Loyalty API — учебный проект для подготовки к интервью (Tesco, .NET Developer / Database Specialist)

Маленький, но "настоящий" ASP.NET Core 8 Web API: баланс баллов лояльности + списание/начисление с защитой от race condition — то есть прямое продолжение архитектурного кейса с собеседования ("касса + мобильное приложение одновременно списывают 500 баллов").

Намеренно покрывает максимум технологий из вакансии: .NET 8, EF Core, MS SQL, REST API, Azure Key Vault, Managed Identity, и готов к деплою через Azure DevOps.

## Структура

```
ClubcardLoyalty.Api/
  Program.cs                     — DI, подключение Key Vault, EF Core
  Models/                        — ClubcardAccount, PointsTransaction, DTO
  Data/LoyaltyDbContext.cs       — EF Core DbContext
  Controllers/ClubcardController.cs — GET balance, POST redeem, POST earn
azure/
  setup-azure-resources.sh       — az CLI: Resource Group, SQL, Key Vault, App Service, Managed Identity
  azure-pipelines.yml            — CI/CD пайплайн для Azure DevOps
```

## Важное замечание про код

Этот код написан и вычитан вручную, но **не собирался `dotnet build` с реальными NuGet-пакетами** — в моей рабочей среде нет доступа к nuget.org (сетевые ограничения sandbox). Структуру и синтаксис я проверил отдельно (offline-сборка против заглушек с такими же сигнатурами, как у настоящих Microsoft.EntityFrameworkCore.SqlServer / Azure.Identity / Azure.Extensions.AspNetCore.Configuration.Secrets) — 0 ошибок. Но на вашей машине, где будет реальный интернет, обязательно сделайте:

```bash
cd ClubcardLoyalty.Api
dotnet restore
dotnet build
```

и поправьте, если EF Core что-то подсветит (версии пакетов могут немного измениться).

## Как запустить локально

1. Поднять SQL Server в Docker:
   ```bash
   docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourLocalPwd123!" \
     -p 1433:1433 --name sql1 -d mcr.microsoft.com/mssql/server:2022-latest
   ```
2. Connection string уже лежит в `appsettings.Development.json` (под локальный docker SQL).
3. Накатить миграции:
   ```bash
   dotnet tool install --global dotnet-ef
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```
4. Вручную завести тестовую карту (одной строкой, через `sqlcmd` или просто INSERT через SSMS/Azure Data Studio):
   ```sql
   INSERT INTO ClubcardAccounts (CardId, CustomerId, Balance, UpdatedUtc, RowVersion)
   VALUES ('CARD-001', 'CUST-001', 500, SYSUTCDATETIME(), DEFAULT);
   ```
5. `dotnet run` — поднимется на `https://localhost:5001` (или порт из launchSettings.json).

## Как воспроизвести race condition из кейса

Это самое полезное упражнение перед интервью — увидеть проблему руками, а потом доказать, что фикс работает.

**Шаг 1 — специально сломать защиту**, чтобы показать гонку: временно замените тело `Redeem` на наивный вариант "прочитать → проверить → вычесть → SaveChanges()" (без атомарного UPDATE). Дайте балансу 500 и одновременно запустите два параллельных запроса по 500:

```bash
curl -s -X POST https://localhost:5001/api/clubcard/CARD-001/redeem \
  -H "Content-Type: application/json" \
  -d '{"amount":500,"channel":1,"idempotencyKey":"key-pos-1"}' &
curl -s -X POST https://localhost:5001/api/clubcard/CARD-001/redeem \
  -H "Content-Type: application/json" \
  -d '{"amount":500,"channel":2,"idempotencyKey":"key-app-1"}' &
wait
```

В наивной версии — оба запроса часто получают 200 OK, баланс уходит в -500.

**Шаг 2** — вернуть текущую реализацию (атомарный `UPDATE ... WHERE Balance >= @amount`) и повторить тот же параллельный запуск: один запрос получит `200 OK` с балансом 0, второй — `409 Conflict` ("Недостаточно баллов"). Баланс никогда не уходит в минус.

**Шаг 3** — повторно отправить **тот же** запрос с тем же `idempotencyKey` ещё раз — получите `200 OK` с уже актуальным балансом, а не повторное списание. Это демонстрирует защиту от ретраев.

## Что показывать на интервью

- Объяснить вслух, почему именно один `UPDATE` с условием в `WHERE`, а не "прочитать-потом-записать" (см. комментарии в `ClubcardController.cs`).
- Объяснить связку Key Vault + Managed Identity в `Program.cs` (секрет `ConnectionStrings--LoyaltyDb`, `DefaultAzureCredential`).
- Пройтись по `azure-pipelines.yml` — рассказать про этапы build/publish/deploy.
- Если спросят про нагрузку в пятницу вечером — упомянуть короткие транзакции, индекс на `(CardId, IdempotencyKey)`, чтение отчётности с read-replica.
