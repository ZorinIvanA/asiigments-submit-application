using LabsApp.Auth.RateLimiting;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Sdk;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-166 (P0, boundary; FR-080, NFR-004) «Потолок ключей и overflow-корзина
/// (детерминированная граница, AR-010)».
///
/// ВНИМАНИЕ (форма сценария): given/when кейса описывают 10004 запроса
/// POST /auth/recovery/request. После ADR-023 (выравнивание времени веток:
/// каждый ДОПУЩЕННЫЙ request выполняет одну PBKDF2-операцию 600000 итераций,
/// в том числе для несуществующих адресов) эндпоинт-форма стоила бы ~10003 KDF
/// (десятки минут CPU) и противоречила бы посылке AC «прогон не порождает
/// PBKDF2» — противоречие эскалировано аналитику (SI-001), и tech solution
/// (ADR-027) перезакрепил сценарий ЗА ПРИКЛАДНЫМ ЛИМИТЕРОМ: попытки подаются
/// ПРЯМЫМИ вызовами RecoveryRequestLimiter.TryConsume из DI — та же нормализация
/// ключа, то же решающее правило окна, тот же потолок MaxTrackedKeys и та же
/// overflow-корзина, без единого вызова KDF. Проверяемые свойства then кейса
/// сохранены: допуск/отказ (true/false ↔ 200/429), размер словаря, отсутствие
/// записей RecoveryCode и событий Email.Dev.
///
/// given: RateLimits__MaxTrackedKeys=10000; словарь лимитера запросов кода пуст;
///        ни один используемый адрес не зарегистрирован (демо-сид выключен,
///        адреса уникальны — санити-проверка первого адреса).
/// when:  10000 попыток recovery/request с уникальными форматно-валидными
///        НЕзарегистрированными email (по одному на ключ); затем 4 попытки с
///        новыми уникальными незарегистрированными адресами; инспекция
///        состояния лимитера и хранилищ.
/// then:  первые 10000 → допуск (размер словаря 10000); 10001-й, 10002-й,
///        10003-й → допуск (первые три попадания в overflow-корзину
///        допускаются); 10004-й → отказ (в эндпоинт-форме — 429 «Слишком много
///        попыток. Повторите позже»); суммарный размер словаря ≤ 10001 записи;
///        записей RecoveryCode и событий Email.Dev не создано.
///        FR-080 AC «Потолок ключей и overflow».
/// </summary>
public sealed class Ts166_KeyCeilingOverflowTests(B05CeilingWebAppFactory factory)
    : IClassFixture<B05CeilingWebAppFactory>
{
    /// <summary>Потолок индивидуальных ключей given кейса.</summary>
    private const int IndividualKeys = 10000;

    private readonly B05CeilingWebAppFactory _factory = factory;

    [Fact]
    public void TS166_CeilingFilledKeys_OverflowsIntoBucketAndRejectsFourth()
    {
        // given: MaxTrackedKeys=10000 (явная настройка фикстуры); словарь пуст;
        //        используемые адреса не зарегистрированы.
        Assert.Equal(
            IndividualKeys,
            _factory.Services.GetRequiredService<IOptions<RateLimitsOptions>>().Value.MaxTrackedKeys);
        var limiter = _factory.Services.GetRequiredService<RecoveryRequestLimiter>();
        Assert.Equal(0, limiter.TrackedKeysCount);
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        Assert.Null(users.GetByEmail(RecoveryEmail(0)));
        _factory.LogSink.Clear();

        // when: 10000 попыток с уникальными форматно-валидными
        //        НЕзарегистрированными email (по одному на ключ).
        for (var index = 0; index < IndividualKeys; index++)
        {
            Assert.True(
                limiter.TryConsume(RecoveryEmail(index)),
                $"Попытка {index + 1} из {IndividualKeys} обязана быть допущена индивидуальным ключом.");
        }

        // then: размер словаря 10000.
        Assert.Equal(IndividualKeys, limiter.TrackedKeysCount);

        // when/then: 10001-й, 10002-й, 10003-й — первые три попадания в
        //            overflow-корзину допускаются (лимит корзины = RequestLimit = 3).
        Assert.Equal(3, RecoveryRequestLimiter.RequestLimit);
        for (var overflow = 0; overflow < RecoveryRequestLimiter.RequestLimit; overflow++)
        {
            Assert.True(
                limiter.TryConsume(OverflowEmail(overflow)),
                $"Overflow-попытка {overflow + 1} обязана быть допущена корзиной.");
        }

        // when/then: 10004-й — отказ (эндпоинт-форма: 429 «Слишком много попыток»);
        //            суммарный размер словаря ≤ 10001 записи.
        Assert.False(
            limiter.TryConsume(OverflowEmail(RecoveryRequestLimiter.RequestLimit)),
            "Четвёртая попытка сверх потолка обязана быть отклонена (в корзине уже 3 метки).");
        Assert.True(
            limiter.TrackedKeysCount <= IndividualKeys + 1,
            $"Суммарный размер словаря обязан быть ≤ {IndividualKeys + 1}, " +
            $"фактически {limiter.TrackedKeysCount}.");
        Assert.Equal(IndividualKeys + 1, limiter.TrackedKeysCount);

        // then: записей RecoveryCode и событий Email.Dev не создано
        //       (адреса не зарегистрированы; KDF в прикладной форме отсутствует).
        Assert.Equal(0, B05SecurityClients.CountRecoveryCodeRecords(_factory));
        Assert.Empty(_factory.LogSink.OfCategory(DevEmailSender.LogCategory));
    }

    /// <summary>Уникальный форматно-валидный незарегистрированный адрес (индивидуальный ключ).</summary>
    private static string RecoveryEmail(int index) => $"ts166-{index:D6}@example.invalid";

    /// <summary>Уникальный форматно-валидный незарегистрированный адрес (после потолка).</summary>
    private static string OverflowEmail(int index) => $"ts166-overflow-{index:D6}@example.invalid";
}
