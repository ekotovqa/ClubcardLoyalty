using ClubcardLoyalty.Api.Data;
using ClubcardLoyalty.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClubcardLoyalty.Tests;

/// <summary>
/// Поднимает реальный HTTP-сервер в памяти — без Docker, без внешних зависимостей.
/// Заменяет SQL Server на SQLite InMemory и отключает Key Vault.
///
/// Почему SQLite, а не EF Core InMemory:
/// - EF Core InMemory не поддерживает ExecuteSqlInterpolatedAsync (raw SQL)
/// - EF Core InMemory не поддерживает BeginTransactionAsync (выбрасывает исключение)
/// - SQLite InMemory — настоящая реляционная БД в памяти: поддерживает и raw SQL, и транзакции
///
/// Важно: SQLite InMemory БД живёт ровно столько, сколько открыт SqliteConnection.
/// Поэтому мы открываем соединение в конструкторе и закрываем в Dispose.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Одно соединение на весь lifetime фабрики — иначе InMemory БД исчезнет между запросами
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Отключаем Key Vault — в тестах Azure недоступен
        builder.UseSetting("KeyVault:Uri", "");

        builder.ConfigureServices(services =>
        {
            // Убираем регистрацию SQL Server DbContext
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<LoyaltyDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Заменяем на SQLite InMemory — поддерживает транзакции и raw SQL
            services.AddDbContext<LoyaltyDbContext>(options =>
                options.UseSqlite(_connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Открываем соединение ДО того, как хост создаст первый scope и потребует DbContext
        _connection.Open();

        var host = base.CreateHost(builder);

        // Создаём схему БД (таблицы, индексы) на основе EF Core модели
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
        db.Database.EnsureCreated();

        return host;
    }

    /// <summary>
    /// Вспомогательный метод — засеять тестовую карту в SQLite БД.
    /// </summary>
    public void SeedCard(string cardId, string customerId, long balance)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
        db.ClubcardAccounts.Add(new ClubcardAccount
        {
            CardId = cardId,
            CustomerId = customerId,
            Balance = balance,
            UpdatedUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        });
        db.SaveChanges();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
