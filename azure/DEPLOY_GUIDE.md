# Как начать работу с Azure и задеплоить ClubcardLoyalty.Api

## Часть 1. Завести Azure

У вас уже есть аккаунт с 2022 года и Free Trial ($200/30 дней) на нём уже использован — повторно его не дадут на тот же аккаунт/карту. Это не проблема: ниже вместо триала используем **бессрочно бесплатные тиры**, которые не зависят от того, использован триал или нет:

- **Azure SQL Database free offer** — до 10 БД на подписку, 100 000 vCore-сек + 32 ГБ данных + 32 ГБ бэкапа бесплатно каждый месяц, без даты окончания, доступно на pay-as-you-go ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql)).
- **App Service F1 (Free)** — $0 навсегда, 60 CPU-мин/день ([Azure Pricing](https://azure.microsoft.com/en-us/pricing/details/app-service/linux/)).
- **Key Vault Standard** — оплата за операции (доли цента за 10 000 вызовов), для пары секретов это копейки.

Я уже поправил `setup-azure-resources.sh` под эти тиры (SQL → serverless free-tier с `AutoPause`, App Service Plan → `F1`). Реального риска внезапного счёта почти нет: SQL при исчерпании лимита просто ставится на паузу, а не списывает деньги.

1. Если аккаунта в портале ещё нет под этим email — заходите как обычно через https://portal.azure.com, выбираете pay-as-you-go (карта потребуется для верификации, но триал предлагать не будет, т.к. уже использован).
2. Зайдите в портал: https://portal.azure.com
3. В портале сверху есть иконка **Cloud Shell** (`>_`). Это браузерный bash с уже установленными az CLI, git, .NET, docker — ничего ставить на свой Windows локально не нужно. Рекомендую стартовать именно через него, особенно учитывая сроки.

   Альтернатива (если хотите работать локально, а не в браузере): поставить Azure CLI на Windows —
   ```powershell
   winget install -e --id Microsoft.AzureCLI
   az login
   ```
   и запускать `.sh`-скрипт либо через Git Bash, либо через WSL (на чистом Windows PowerShell bash-скрипт не запустится).

## Часть 2. Запушить проект в GitHub

Раз в вакансии явно требуется GitHub — заведите репозиторий и сразу там же потренируйтесь:

```bash
cd ClubcardLoyalty.Api   # папка с проектом, который я собрал
git init
git add .
git commit -m "Initial commit: Clubcard Loyalty API"
git branch -M main
git remote add origin https://github.com/<ваш-username>/clubcard-loyalty-api.git
git push -u origin main
```

(репозиторий на GitHub предварительно создайте через сайт — Create repository, без README, чтобы не было конфликта).

## Часть 3. Создать ресурсы в Azure

1. В Cloud Shell клонируйте репозиторий:
   ```bash
   git clone https://github.com/<ваш-username>/clubcard-loyalty-api.git
   cd clubcard-loyalty-api/azure
   ```
2. **Откройте `setup-azure-resources.sh` и смените `SQL_ADMIN_PASS`** на свой надёжный пароль — это обязательно, дефолтное значение в файле для примера.
3. Запустите:
   ```bash
   chmod +x setup-azure-resources.sh
   ./setup-azure-resources.sh
   ```
   В Cloud Shell вы уже авторизованы (`az login` не нужен). Скрипт создаст: Resource Group → SQL Server + Database (бесплатный serverless-тир) → Key Vault → App Service Plan (Linux, F1 Free) → Web App → Managed Identity → RBAC-доступ к Key Vault → секрет с connection string.
4. В конце скрипт выведет имена ресурсов и URL веб-приложения — сохраните их.

## Часть 4. Накатить схему БД на Azure SQL

Прямо там же, в Cloud Shell (firewall-правило "Allow Azure services", которое создал скрипт, пускает именно такой трафик):

```bash
cd ~/clubcard-loyalty-api/ClubcardLoyalty.Api
dotnet tool install --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet restore
dotnet ef database update --connection "<строка подключения из шага 3, та же, что положили в Key Vault>"
```

Если `dotnet ef` ругается, что миграций ещё нет — сначала локально (или тут же в Cloud Shell) выполните `dotnet ef migrations add InitialCreate`, закоммитьте папку `Migrations/` в git, и повторите `database update`.

## Часть 5. Настроить Azure DevOps и задеплоить через pipeline

1. https://dev.azure.com → Create organization (бесплатно) → New project (например, `clubcard-loyalty`).
2. **Service connection**: Project Settings → Service connections → New service connection → Azure Resource Manager → Workload identity federation (рекомендуемый, автоматический) → выбрать вашу подписку → назвать `clubcard-azure-conn` (это имя уже стоит в `azure-pipelines.yml`, если назовёте иначе — поправьте переменную `azureSubscription` в файле).
3. Pipelines → New pipeline → GitHub → авторизовать GitHub App, если попросит → выбрать репозиторий `clubcard-loyalty-api` → "Existing Azure Pipelines YAML file" → путь `/azure/azure-pipelines.yml`.
4. В самом yml поправьте `webAppName` на реальное имя вашего Web App из шага 3 части 3.
5. Save and run. Pipeline пройдёт стадии Build → Deploy.

## Часть 6. Проверить

```bash
curl https://<ваш-webapp>.azurewebsites.net/api/clubcard/CARD-001/balance
```

Сначала вручную добавьте тестовую карту в БД (см. README.md в проекте — INSERT в `ClubcardAccounts`).

## Часть 7. Про деньги

Без триала всё, что не free-tier, списывается с первой минуты — поэтому весь раннбук теперь сидит на бессрочно бесплатных SKU (SQL serverless free offer + App Service F1). Риск случайного счёта низкий, но не нулевой, держите в голове:

- SQL: при исчерпании 100k vCore-сек/32 ГБ в месяц — `AutoPause`, БД просто недоступна до начала следующего месяца, **не биллинг**.
- App Service F1: жёсткий лимит 60 CPU-мин/день, выше — Azure просто притормаживает/блокирует app до следующих суток, не списывает деньги.
- Key Vault Standard: единственное, что технически платное (доли цента за 10k операций) — для пары секретов это не дойдёт даже до $0.01.

Когда закончите тренироваться — снесите всё одной командой (хорошая привычка независимо от тарифа):

```bash
az group delete --name rg-clubcard-loyalty --yes --no-wait
```

Azure DevOps Basic-план (до 5 пользователей, неограниченные приватные репозитории) бесплатен сам по себе и не завязан на SQL/App Service тиры — за него можно не переживать.

## Если что-то упадёт

Это нормально и даже хорошо — реальные ошибки деплоя — отличный материал для интервью ("расскажите о проблеме, которую вы решали самостоятельно"). Принесите мне точный текст ошибки — разберём вместе и я объясню, что бы это значило с точки зрения интервьюера.
