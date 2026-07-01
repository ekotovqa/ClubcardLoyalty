using System.Data;
using ClubcardLoyalty.Api.Data;
using ClubcardLoyalty.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ClubcardLoyalty.Api.Controllers;

[ApiController]
[Route("api/clubcard/{cardId}")]
public class ClubcardController : ControllerBase
{
    private readonly LoyaltyDbContext _db;

    public ClubcardController(LoyaltyDbContext db)
    {
        _db = db;
    }

    [HttpGet("balance")]
    public async Task<ActionResult<BalanceResponse>> GetBalance(string cardId)
    {
        var account = await _db.ClubcardAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.CardId == cardId);

        if (account is null)
        {
            return NotFound();
        }

        return Ok(new BalanceResponse(account.CardId, account.Balance, account.UpdatedUtc));
    }

    /// <summary>
    /// Списание баллов. Ключевая часть кейса: касса и мобильное приложение могут
    /// одновременно дёрнуть этот эндпоинт для одной и той же карты.
    ///
    /// Решение:
    /// 1) Списание — один атомарный UPDATE с условием Balance >= @amount в WHERE.
    ///    SQL Server берёт эксклюзивную блокировку строки на время самого UPDATE,
    ///    так что параллельный второй запрос просто ждёт commit первого и видит
    ///    уже актуальный баланс — read-then-write гонки физически нет.
    /// 2) IdempotencyKey + уникальный индекс защищают от повторной обработки одного
    ///    и того же запроса при ретрае по сети (а не только от честной конкуренции).
    /// </summary>
    [HttpPost("redeem")]
    public async Task<IActionResult> Redeem(
        string cardId,
        [FromBody] RedeemRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { message = "Idempotency-Key header is required." });

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        try
        {
            var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ClubcardAccounts
                SET Balance = Balance - {request.Amount}, UpdatedUtc = SYSUTCDATETIME()
                WHERE CardId = {cardId} AND Balance >= {request.Amount}");

            if (rowsAffected == 0)
            {
                await tx.RollbackAsync();

                var exists = await _db.ClubcardAccounts.AsNoTracking()
                    .AnyAsync(a => a.CardId == cardId);

                return exists
                    ? Conflict(new { message = "Недостаточно баллов на карте." })
                    : NotFound();
            }

            _db.PointsTransactions.Add(new PointsTransaction
            {
                CardId = cardId,
                Amount = -request.Amount,
                Type = TransactionType.Redeem,
                Channel = request.Channel,
                IdempotencyKey = idempotencyKey,
                CreatedUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Тот же IdempotencyKey для этой карты уже есть в таблице транзакций —
            // значит запрос уже был обработан (например, клиент ретраил после таймаута).
            // Возвращаем текущий баланс, а не ошибку — это и есть идемпотентность.
            await tx.RollbackAsync();

            var account = await _db.ClubcardAccounts.AsNoTracking()
                .FirstAsync(a => a.CardId == cardId);

            return Ok(new BalanceResponse(account.CardId, account.Balance, account.UpdatedUtc));
        }

        var updated = await _db.ClubcardAccounts.AsNoTracking().FirstAsync(a => a.CardId == cardId);
        return Ok(new BalanceResponse(updated.CardId, updated.Balance, updated.UpdatedUtc));
    }

    [HttpPost("earn")]
    public async Task<IActionResult> Earn(
        string cardId,
        [FromBody] EarnRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { message = "Idempotency-Key header is required." });

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ClubcardAccounts
            SET Balance = Balance + {request.Amount}, UpdatedUtc = SYSUTCDATETIME()
            WHERE CardId = {cardId}");

        if (rowsAffected == 0)
        {
            return NotFound();
        }

        _db.PointsTransactions.Add(new PointsTransaction
        {
            CardId = cardId,
            Amount = request.Amount,
            Type = TransactionType.Earn,
            Channel = request.Channel,
            IdempotencyKey = idempotencyKey,
            CreatedUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var updated = await _db.ClubcardAccounts.AsNoTracking().FirstAsync(a => a.CardId == cardId);
        return Ok(new BalanceResponse(updated.CardId, updated.Balance, updated.UpdatedUtc));
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException is SqlException { Number: 2601 or 2627 };
}
