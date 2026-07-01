using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClubcardLoyalty.Api.Models;
using Xunit;

namespace ClubcardLoyalty.Tests;

/// <summary>
/// Интеграционные тесты: реальный HTTP-сервер в памяти, реальный pipeline ASP.NET Core
/// (включая middleware), реальные контроллеры — только БД заменена на InMemory.
/// </summary>
public class ClubcardControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public ClubcardControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── GET /balance ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBalance_ExistingCard_Returns200WithBalance()
    {
        var cardId = $"CARD-{Guid.NewGuid()}";
        _factory.SeedCard(cardId, "CUST-001", 1000);

        var response = await _client.GetAsync($"/api/clubcard/{cardId}/balance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BalanceResponse>();
        Assert.NotNull(body);
        Assert.Equal(cardId, body!.CardId);
        Assert.Equal(1000, body.Balance);
    }

    [Fact]
    public async Task GetBalance_NonExistentCard_Returns404()
    {
        var response = await _client.GetAsync("/api/clubcard/CARD-NONEXISTENT/balance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── POST /earn ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Earn_ValidRequest_IncreasesBalance()
    {
        var cardId = $"CARD-{Guid.NewGuid()}";
        _factory.SeedCard(cardId, "CUST-001", 500);

        var response = await PostWithKey(
            $"/api/clubcard/{cardId}/earn",
            new { amount = 300, channel = 1 },
            idempotencyKey: Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BalanceResponse>();
        Assert.Equal(800, body!.Balance);
    }

    [Fact]
    public async Task Earn_MissingIdempotencyKey_Returns400()
    {
        var cardId = $"CARD-{Guid.NewGuid()}";
        _factory.SeedCard(cardId, "CUST-001", 500);

        // Запрос без заголовка Idempotency-Key
        var content = new StringContent(
            JsonSerializer.Serialize(new { amount = 100, channel = 1 }),
            Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/clubcard/{cardId}/earn", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── POST /redeem ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Redeem_ValidRequest_DecreasesBalance()
    {
        var cardId = $"CARD-{Guid.NewGuid()}";
        _factory.SeedCard(cardId, "CUST-001", 1000);

        var response = await PostWithKey(
            $"/api/clubcard/{cardId}/redeem",
            new { amount = 300, channel = 2 },
            idempotencyKey: Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BalanceResponse>();
        Assert.Equal(700, body!.Balance);
    }

    [Fact]
    public async Task Redeem_InsufficientBalance_Returns409()
    {
        var cardId = $"CARD-{Guid.NewGuid()}";
        _factory.SeedCard(cardId, "CUST-001", 100);

        var response = await PostWithKey(
            $"/api/clubcard/{cardId}/redeem",
            new { amount = 9999, channel = 2 },
            idempotencyKey: Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Redeem_NonExistentCard_Returns404()
    {
        var response = await PostWithKey(
            "/api/clubcard/CARD-GHOST/redeem",
            new { amount = 100, channel = 2 },
            idempotencyKey: Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Идемпотентность ───────────────────────────────────────────────────────

    [Fact]
    public async Task Redeem_SameIdempotencyKey_DoesNotDeductTwice()
    {
        // Ключевой тест: гарантия что повторный запрос (ретрай по сети)
        // не приводит к двойному списанию.
        var cardId = $"CARD-{Guid.NewGuid()}";
        _factory.SeedCard(cardId, "CUST-001", 1000);
        var key = Guid.NewGuid().ToString();

        // Первый запрос — реальное списание
        var first = await PostWithKey(
            $"/api/clubcard/{cardId}/redeem",
            new { amount = 200, channel = 2 },
            idempotencyKey: key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<BalanceResponse>();
        Assert.Equal(800, firstBody!.Balance);

        // Второй запрос с тем же ключом — middleware возвращает кэш
        var second = await PostWithKey(
            $"/api/clubcard/{cardId}/redeem",
            new { amount = 200, channel = 2 },
            idempotencyKey: key);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<BalanceResponse>();

        // Баланс не изменился — списания не было
        Assert.Equal(800, secondBody!.Balance);

        // Финальная проверка через GET
        var balanceCheck = await _client.GetAsync($"/api/clubcard/{cardId}/balance");
        var final = await balanceCheck.Content.ReadFromJsonAsync<BalanceResponse>();
        Assert.Equal(800, final!.Balance);
    }

    // ── Вспомогательный метод ─────────────────────────────────────────────────

    private Task<HttpResponseMessage> PostWithKey(string url, object body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return _client.SendAsync(request);
    }
}
