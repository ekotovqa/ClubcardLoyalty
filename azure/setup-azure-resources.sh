#!/usr/bin/env bash
# Раннбук для ручного прогона по шагам (не запускайте всё сразу бездумно —
# смысл упражнения в том, чтобы понимать каждую команду на интервью).
#
# Перед запуском: az login
set -euo pipefail

# ---- переменные — поменяйте под себя ----
RG="rg-clubcard-loyalty"
LOCATION="westeurope"
SQL_SERVER="sql-clubcard-$RANDOM"          # имя SQL-сервера должно быть глобально уникальным
SQL_DB="ClubcardLoyalty"
SQL_ADMIN_USER="loyaltyadmin"
SQL_ADMIN_PASS="ChangeMe!2026StrongPwd"     # обязательно смените перед реальным запуском
KEYVAULT="kv-clubcard-$RANDOM"              # имя Key Vault тоже глобально уникальное, 3-24 символа
APP_SERVICE_PLAN="asp-clubcard"
WEBAPP="app-clubcard-$RANDOM"

# ---- 1. Resource Group ----
az group create --name "$RG" --location "$LOCATION"

# ---- 2. SQL Server + Database — у вас уже использован Free Trial, поэтому берём
#         "Azure SQL Database free offer": он НЕ привязан к триалу, доступен на любой
#         подписке (pay-as-you-go тоже). До 10 БД на подписку, 100 000 vCore-секунд +
#         32 ГБ данных + 32 ГБ бэкапа бесплатно КАЖДЫЙ МЕСЯЦ, бессрочно. При исчерпании
#         лимита AutoPause просто ставит БД на паузу до начала след. месяца — без списаний ----
az sql server create \
  --name "$SQL_SERVER" \
  --resource-group "$RG" \
  --location "$LOCATION" \
  --admin-user "$SQL_ADMIN_USER" \
  --admin-password "$SQL_ADMIN_PASS"

# Разрешить доступ из Azure-сервисов (App Service) к SQL Server
az sql server firewall-rule create \
  --resource-group "$RG" \
  --server "$SQL_SERVER" \
  --name "AllowAzureServices" \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0

az sql db create \
  --resource-group "$RG" \
  --server "$SQL_SERVER" \
  --name "$SQL_DB" \
  --edition GeneralPurpose \
  --family Gen5 \
  --capacity 2 \
  --compute-model Serverless \
  --use-free-limit \
  --free-limit-exhaustion-behavior AutoPause

# ---- 3. Key Vault ----
az keyvault create \
  --name "$KEYVAULT" \
  --resource-group "$RG" \
  --location "$LOCATION" \
  --enable-rbac-authorization true

# ---- 4. App Service Plan (Linux, F1 Free) + Web App (.NET 8) ----
# F1 — бессрочно бесплатный тир (не привязан к триалу): 60 CPU-мин/день, 1 ГБ RAM,
# приложение "засыпает" после 20 мин без запросов и просыпается на следующий запрос
# (для учебного API на интервью — более чем достаточно; нет custom domain SSL).
az appservice plan create \
  --name "$APP_SERVICE_PLAN" \
  --resource-group "$RG" \
  --location "$LOCATION" \
  --sku F1 \
  --is-linux

az webapp create \
  --name "$WEBAPP" \
  --resource-group "$RG" \
  --plan "$APP_SERVICE_PLAN" \
  --runtime "DOTNETCORE:8.0"

# ---- 5. System-assigned Managed Identity для Web App ----
PRINCIPAL_ID=$(az webapp identity assign \
  --name "$WEBAPP" \
  --resource-group "$RG" \
  --query principalId -o tsv)

echo "Managed Identity principalId: $PRINCIPAL_ID"

# ---- 6. Дать Managed Identity право читать секреты из Key Vault (RBAC, не access policy) ----
KV_ID=$(az keyvault show --name "$KEYVAULT" --resource-group "$RG" --query id -o tsv)

az role assignment create \
  --assignee "$PRINCIPAL_ID" \
  --role "Key Vault Secrets User" \
  --scope "$KV_ID"

# ---- 7. Положить connection string в Key Vault ----
# Имя секрета "ConnectionStrings--LoyaltyDb" — двойной дефис вместо ":",
# чтобы провайдер конфигурации .NET смог замапить его на ConnectionStrings:LoyaltyDb.
CONN_STRING="Server=tcp:${SQL_SERVER}.database.windows.net,1433;Initial Catalog=${SQL_DB};Persist Security Info=False;User ID=${SQL_ADMIN_USER};Password=${SQL_ADMIN_PASS};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

az keyvault secret set \
  --vault-name "$KEYVAULT" \
  --name "ConnectionStrings--LoyaltyDb" \
  --value "$CONN_STRING"

# ---- 8. Сказать приложению, где искать Key Vault (это НЕ секрет, обычная app setting) ----
az webapp config appsettings set \
  --name "$WEBAPP" \
  --resource-group "$RG" \
  --settings "KeyVault__Uri=https://${KEYVAULT}.vault.azure.net/"

echo ""
echo "=== Готово ==="
echo "Resource Group:   $RG"
echo "SQL Server:       $SQL_SERVER.database.windows.net"
echo "Key Vault:        $KEYVAULT"
echo "Web App:          https://${WEBAPP}.azurewebsites.net"
echo ""
echo "Дальше: настройте Azure DevOps service connection на эту подписку"
echo "и подставьте имя Web App / Resource Group в azure-pipelines.yml."
