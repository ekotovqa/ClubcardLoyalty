using Azure.Identity;
using ClubcardLoyalty.Api.Data;
using ClubcardLoyalty.Api.Middleware;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Key Vault: подключаем ДО чтения connection string, чтобы секреты из Key Vault
// перекрыли/дополнили appsettings.json. DefaultAzureCredential сам разберётся:
// - в Azure App Service возьмёт System-assigned Managed Identity
// - локально на машине разработчика возьмёт `az login` (Azure CLI credential)
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}

// Секрет в Key Vault называется "ConnectionStrings--LoyaltyDb" (двойной дефис,
// т.к. ":" недопустим в имени секрета Key Vault). Провайдер сам превращает
// "--" обратно в ":", поэтому GetConnectionString("LoyaltyDb") отрабатывает как обычно.
builder.Services.AddDbContext<LoyaltyDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("LoyaltyDb")));

builder.Services.AddMemoryCache();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseMiddleware<IdempotencyMiddleware>();
app.MapControllers();

app.Run();

// Нужно для WebApplicationFactory в интеграционных тестах:
// тип Program в minimal hosting генерируется как internal,
// partial class делает его доступным из тестового проекта.
public partial class Program { }
