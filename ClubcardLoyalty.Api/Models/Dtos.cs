using System.ComponentModel.DataAnnotations;

namespace ClubcardLoyalty.Api.Models;

public class RedeemRequest
{
    [Range(1, long.MaxValue)]
    public long Amount { get; set; }

    public Channel Channel { get; set; }

    // Клиент (касса/приложение) должен присылать один и тот же ключ при ретрае
    // одного и того же логического запроса (например, GUID, сгенерированный на клиенте).
    [Required]
    public string IdempotencyKey { get; set; } = default!;
}

public class EarnRequest
{
    [Range(1, long.MaxValue)]
    public long Amount { get; set; }

    public Channel Channel { get; set; }

    [Required]
    public string IdempotencyKey { get; set; } = default!;
}

public record BalanceResponse(string CardId, long Balance, DateTime UpdatedUtc);
