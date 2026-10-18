using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B02.Infrastructure;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-137 «Сид: Production со своим паролем — только преподаватель, без демо-данных»
/// (FR-025 AC «Production со своим паролем стартует», «Production без демо-данных»,
/// P1; тип happy_path).
///
/// given: Environment=Production; Seed__TeacherPassword='Str0ng!Unique2026';
///        Auth__JwtKey задан (валидные умолчания фабрики для Production);
///        Seed__DemoData НЕ задан (useHarnessDefaults:false — харнес-умолчание
///        «Seed__DemoData=false» не применяется, работает Production-умолчание
///        приложения: false по FR-025; иначе демаскировалась бы проверяемая
///        ветка «Production без демо-данных»).
/// when:  старт приложения; вход teacher/Str0ng!Unique2026; инспекция хранилищ
///        групп/работ/сдач (и пользователей — «создан только преподаватель»).
/// then:  приложение стартует; вход — 200; создан только преподаватель — хранилища
///        групп/работ/сдач пусты, студентов нет.
///
/// Инспекция хранилищ — через DI тестового хоста (I*Repository — те же singleton'ы,
/// что читают контроллеры). Дополнительно списки групп/работ проверяются и через API
/// teacher: сессия берётся из Set-Cookie реального ответа входа и подставляется
/// заголовком Cookie (клиент с HandleCookies=false). Cookie-контейнер не подходит:
/// в Production access-cookie выпускается с флагом Secure (FR-008/NFR-007), а реплей
/// Secure-cookie по http://localhost контейнером блокируется — против корректной
/// реализации оба GET ушли бы без access_token и получили 401.
/// </summary>
public sealed class Ts137_ProductionOwnPasswordTeacherOnlyTests
{
    /// <summary>Нестандартный сид-пароль кейса (удовлетворяет правилам пароля §8).</summary>
    private const string OwnTeacherPassword = "Str0ng!Unique2026";

    [Fact]
    public async Task ProductionWithOwnPassword_Starts_LogsInTeacherOnly_NoDemoData()
    {
        // given: Production-хост с валидным Auth__JwtKey и своим Seed__TeacherPassword.
        // useHarnessDefaults:false — Seed__DemoData остаётся не заданным, проверяется
        // именно Production-умолчание приложения («без демо-данных», FR-025), а не
        // принудительное харнес-умолчание фабрики.
        using var factory = new B02WebAppFactory(
            Environments.Production,
            useHarnessDefaults: false,
            settings: new Dictionary<string, string?>
            {
                ["Seed__TeacherPassword"] = OwnTeacherPassword,
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false, // Secure-cookie по http контейнером не реплится (NFR-007).
        });

        // when: вход teacher/Str0ng!Unique2026.
        using var login = await HostClients.LoginAsync(client, SeedOptions.DefaultTeacherLogin, OwnTeacherPassword);

        // then: вход — 200.
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // then: сессия — access_token из Set-Cookie ответа входа (подстановка вручную).
        var accessToken = HostClients.ExtractAccessToken(login);
        Assert.False(string.IsNullOrEmpty(accessToken), "Вход не выпустил access_token cookie.");

        // when: инспекция хранилищ групп/работ/сдач и пользователей.
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var labs = factory.Services.GetRequiredService<ILabRepository>();
        var submissions = factory.Services.GetRequiredService<ISubmissionRepository>();

        // then: создан только преподаватель — студентов нет; группы/работы/сдачи
        // отсутствуют (списки пусты; демо-данных нет).
        Assert.NotNull(users.GetByLogin(SeedOptions.DefaultTeacherLogin));
        Assert.Empty(users.ListStudents());
        Assert.Empty(groups.GetAll());
        var storedLabs = labs.GetAll();
        Assert.Empty(storedLabs);
        Assert.Empty(submissions.ListByLabIds(storedLabs.Select(lab => lab.Id).ToList()));

        // then (контрольный срез через API teacher): списки групп и работ пусты.
        using var groupsResponse = await SendUnderTeacherAsync(client, accessToken!, "/api/v1/groups");
        using var labsResponse = await SendUnderTeacherAsync(client, accessToken!, "/api/v1/labs");
        Assert.Equal(HttpStatusCode.OK, groupsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, labsResponse.StatusCode);

        var groupsPayload = JsonDocument.Parse(await groupsResponse.Content.ReadAsStringAsync()).RootElement;
        var labsPayload = JsonDocument.Parse(await labsResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, ApiShapes.TotalOf(groupsPayload));
        Assert.Equal(0, ApiShapes.ItemsOf(groupsPayload).GetArrayLength());
        Assert.Equal(0, ApiShapes.TotalOf(labsPayload));
        Assert.Equal(0, ApiShapes.ItemsOf(labsPayload).GetArrayLength());
    }

    /// <summary>GET под teacher: access-cookie из ответа входа заголовком Cookie.</summary>
    private static async Task<HttpResponseMessage> SendUnderTeacherAsync(
        HttpClient client, string accessToken, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(
            "Cookie", $"{AuthCoreDefaults.AccessTokenCookieName}={accessToken}");
        return await client.SendAsync(request);
    }
}
