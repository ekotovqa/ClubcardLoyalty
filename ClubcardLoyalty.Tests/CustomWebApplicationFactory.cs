using ClubcardLoyalty.Api.Data;
using ClubcardLoyalty.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClubcardLoyalty.Tests;

/// <summary>
/// Поднимает реальный HTTP-сервер в памяти — без Docker, без внешних зависимостей.
/// Заменяет SQL Server на EF Core InMemory и отключает Key Vault.
/// Каждый экземпляр фабрики использует изолированную БД (уникальное имя).
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Уникальная БД на каждый экземпляр фабрики — тесты не мешают друг другу
    private readonly string _dbName = Guid.NewGuid().ToString();

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

            // Заменяем на InMemory — без реального SQL Server
            services.AddDbContext<LoyaltyDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }

    /// <summary>
    /// Вспомогательный метод — засеять тестовую карту прямо в InMemory БД.
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
            RowVersion = Array.Empty<byte>()
        });
        db.SaveChanges();
    }
}
