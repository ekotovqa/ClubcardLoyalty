using System.ComponentModel.DataAnnotations;

namespace ClubcardLoyalty.Api.Models;

public class RedeemRequest
{
    [Range(1, long.MaxValue)]
    public long Amount { get; set; }

    public Channel Channel { get; set; }
}

public class EarnRequest
{
    [Range(1, long.MaxValue)]
    public long Amount { get; set; }

    public Channel Channel { get; set; }
}

public record BalanceResponse(string CardId, long Balance, DateTime UpdatedUtc);
