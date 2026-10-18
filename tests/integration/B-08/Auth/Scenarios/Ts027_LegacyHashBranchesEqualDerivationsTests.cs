using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B08.Auth.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

/// <summary>
/// TS-027 «Legacy-хэш: равное число дериваций в ветках Verify и VerifyReference»
/// (boundary, FR-005 + FR-007, P2).
///
/// given: пользователь создан при Auth__Pbkdf2Iterations=1000; конфигурация
///        процесса изменена на 2000; счётчик KDF сбрасывается перед каждым
///        запросом.
/// when:  неудачный вход по этому логину (ветка Verify); неудачный вход по
///        неизвестному логину (ветка VerifyReference).
/// then:  оба — 401 'Неверный логин или пароль'; Δkdf=1 на каждый запрос;
///        расхождение времени выполнения веток НЕ утверждается
///        (документированная остаточная поверхность AR-004/ASM-020).
///
/// «Счётчик сбрасывается перед каждым запросом» — базовая линия Snapshot()
/// перед каждым HTTP-шагом (IKdfCounter сброса не имеет; методика Δkdf FR-027).
/// Тайминговые утверждения кейс сознательно НЕ содержит (AR-004: гарантия
/// равномерности — только для хэшей текущей конфигурации; расхождение веток на
/// legacy-хэше принято и задокументировано — ASM-020).
/// </summary>
public sealed class Ts027_LegacyHashBranchesEqualDerivationsTests
{
    private const string LegacyLogin = "legacybranch";
    private const string LegacyEmail = "legacybranch@example.com";
    private const string UnknownLogin = "ghostbranch";
    private const string WrongPassword = "Wrong0rd!";

    [Fact]
    public async Task FailedLogin_LegacyVerifyAndReferenceBranches_SingleDerivationEach()
    {
        using var factory = new B08AuthDevFactory();
        using var client = B08AuthHost.CreateClient(factory);

        // given: пользователь создан при Auth__Pbkdf2Iterations=1000 (DI-сид —
        // реальный IPasswordHasher фикстуры, метка seed).
        B08AuthHost.SeedUser(
            factory, LegacyLogin, LegacyEmail, "Легаси Ветка", LabsApp.Domain.Entities.UserRoles.Student);

        // given: конфигурация процесса изменена на 2000; хранилище не пересоздано.
        var authOptions = factory.Services.GetRequiredService<IOptions<AuthOptions>>();
        authOptions.Value.Pbkdf2Iterations = 2000;

        var counter = factory.Services.GetRequiredService<IKdfCounter>();

        // when: неудачный вход по этому логину (ветка Verify — пользователь найден);
        // счётчик KDF «сброшен» — базовая линия перед запросом.
        var beforeVerify = counter.Snapshot();
        using var knownLogin = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            "{\"login\":\"" + LegacyLogin + "\",\"password\":\"" + WrongPassword + "\"}");

        // then: 401 'Неверный логин или пароль'; Δkdf=1 (метка login; reference не трогается).
        B08AuthHost.AssertStatus(knownLogin, HttpStatusCode.Unauthorized, "неудачный вход по известному логину (ветка Verify)");
        var knownBody = await B08AuthHost.ReadJsonObjectAsync(knownLogin, "тело 401 ветки Verify (TS-027)");
        Assert.Equal(B08AuthHost.WrongCredentialsMessage, B08AuthHost.StringProperty(knownBody, "message"));

        var verifyDelta = B08AuthHost.KdfDelta(beforeVerify, counter.Snapshot());
        Assert.Equal(1, verifyDelta.Total());
        Assert.Equal(1, verifyDelta.GetValueOrDefault(KdfCallers.Login));
        Assert.Equal(0, verifyDelta.GetValueOrDefault(KdfCallers.Reference));

        // when: неудачный вход по неизвестному логину (ветка VerifyReference);
        // счётчик KDF «сброшен» — новая базовая линия перед запросом.
        var beforeReference = counter.Snapshot();
        using var unknownLogin = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            "{\"login\":\"" + UnknownLogin + "\",\"password\":\"" + WrongPassword + "\"}");

        // then: 401 с тем же текстом; Δkdf=1 (метка reference; login не трогается).
        B08AuthHost.AssertStatus(unknownLogin, HttpStatusCode.Unauthorized, "неудачный вход по неизвестному логину (ветка VerifyReference)");
        var unknownBody = await B08AuthHost.ReadJsonObjectAsync(unknownLogin, "тело 401 ветки VerifyReference (TS-027)");
        Assert.Equal(B08AuthHost.WrongCredentialsMessage, B08AuthHost.StringProperty(unknownBody, "message"));

        var referenceDelta = B08AuthHost.KdfDelta(beforeReference, counter.Snapshot());
        Assert.Equal(1, referenceDelta.Total());
        Assert.Equal(1, referenceDelta.GetValueOrDefault(KdfCallers.Reference));
        Assert.Equal(0, referenceDelta.GetValueOrDefault(KdfCallers.Login));

        // then (протокольная часть кейса): на каждый запрос ровно одна деривация
        // в ОБОИХ ветках; расхождение времени выполнения веток НЕ утверждается
        // (AR-004/ASM-020 — документированная остаточная поверхность).
    }
}
