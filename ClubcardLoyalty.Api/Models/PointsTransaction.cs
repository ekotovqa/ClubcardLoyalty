namespace ClubcardLoyalty.Api.Models;

public enum TransactionType
{
    Earn = 1,
    Redeem = 2
}

public enum Channel
{
    Pos = 1,
    MobileApp = 2,
    Web = 3
}

/// <summary>
/// Неизменяемая запись в "леджере". Balance в ClubcardAccount можно в любой момент
/// пересчитать из суммы этих записей — это даёт аудируемость (важно для баллов лояльности).
/// Уникальный индекс на (CardId, IdempotencyKey) — защита от повторной обработки
/// одного и того же запроса при ретрае по сети.
/// </summary>
public class PointsTransaction
{
    public long Id { get; set; }
    public string CardId { get; set; } = default!;

    // Положительное значение — начисление, отрицательное — списание.
    public long Amount { get; set; }
    public TransactionType Type { get; set; }
    public Channel Channel { get; set; }
    public string IdempotencyKey { get; set; } = default!;
    public DateTime CreatedUtc { get; set; }
}
