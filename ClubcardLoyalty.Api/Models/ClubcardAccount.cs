using System.ComponentModel.DataAnnotations;

namespace ClubcardLoyalty.Api.Models;

/// <summary>
/// Материализованный баланс карты. Источник правды — таблица PointsTransactions,
/// но хранить агрегат отдельно нужно, чтобы не пересчитывать сумму по всей истории
/// на каждый запрос баланса.
/// </summary>
public class ClubcardAccount
{
    public string CardId { get; set; } = default!;
    public string CustomerId { get; set; } = default!;
    public long Balance { get; set; }
    public DateTime UpdatedUtc { get; set; }

    // RowVersion даёт EF Core optimistic concurrency "из коробки":
    // EF добавит "WHERE RowVersion = @original" в UPDATE и бросит
    // DbUpdateConcurrencyException, если кто-то успел изменить строку раньше.
    // В Redeem-эндпоинте мы используем другой механизм (атомарный raw SQL UPDATE),
    // но колонка остаётся как защита для остальных операций с этой сущностью
    // (например, если кто-то отредактирует баланс через админку).
    [Timestamp]
    public byte[] RowVersion { get; set; } = default!;
}
