using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace ClubcardLoyalty.Api.Middleware;

/// <summary>
/// Перехватывает POST-запросы с заголовком Idempotency-Key.
/// Первый запрос — пропускается до контроллера, ответ кэшируется.
/// Повторный запрос с тем же ключом — возвращает кэш, контроллер не вызывается.
///
/// Хранилище: IMemoryCache (в памяти одного инстанса).
/// В проде с несколькими инстансами App Service нужен IDistributedCache (Redis).
/// </summary>
public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;

    public IdempotencyMiddleware(RequestDelegate next, IMemoryCache cache)
    {
        _next = next;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Только POST-запросы
        if (context.Request.Method != HttpMethods.Post)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues))
        {
            // Нет заголовка — пропускаем дальше, контроллер вернёт 400
            await _next(context);
            return;
        }

        var key = keyValues.ToString();

        // Есть в кэше — возвращаем закэшированный ответ, до контроллера не доходим
        if (_cache.TryGetValue(key, out CachedResponse? cached))
        {
            context.Response.StatusCode = cached!.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(cached.Body, Encoding.UTF8);
            return;
        }

        // Подменяем Response.Body на буфер, чтобы перехватить ответ контроллера
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await _next(context);

        // Читаем что записал контроллер
        buffer.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();

        // Кэшируем только успешные ответы (2xx).
        // 4xx/5xx не кэшируем: например, 409 (недостаточно баллов) не должен
        // блокировать повторный запрос, если баланс пополнили.
        if (context.Response.StatusCode is >= 200 and < 300)
        {
            _cache.Set(key, new CachedResponse(context.Response.StatusCode, responseBody),
                TimeSpan.FromHours(24));
        }

        // Восстанавливаем оригинальный поток и копируем ответ
        context.Response.Body = originalBody;
        buffer.Seek(0, SeekOrigin.Begin);
        await buffer.CopyToAsync(originalBody);
    }
}

internal record CachedResponse(int StatusCode, string Body);
