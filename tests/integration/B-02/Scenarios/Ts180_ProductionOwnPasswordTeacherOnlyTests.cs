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
/// TS-180 «Production со своим паролем: только преподаватель, вход работает»
/// (FR-025 AC «Production со своим паролем стартует» + «Production без
/// демо-данных», P1; тип happy_path).
///
/// given: Environment=Production; Seed__TeacherPassword='Str0ng!Unique2026';
///        Auth__JwtKey задан (валидные умолчания фабрики для Production);
///        Seed__DemoData НЕ задан (useHarnessDefaults:false — работает Production-
///        умолчание приложения: false по FR-025).
/// when:  старт приложения; вход teacher/Str0ng!Unique2026; GET /groups и GET /labs
///        под teacher; инспекция хранилищ пользователей/групп/работ/сдач.
/// then:  приложение стартует; вход — 200; создан только преподаватель — списки
///        групп и работ пусты.
///
/// Cookie-контейнер не подходит: в Production access-cookie выпускается с флагом
/// Secure (FR-008/NFR-007), а реплей Secure-cookie по http://localhost контейнером
/// блокируется — сессия из Set-Cookie ответа входа подставляется заголовком Cookie
/// на клиенте с HandleCookies=false (иначе оба GET ушли бы без access_token — 401).
/// </summary>
public sealed class Ts180_ProductionOwnPasswordTeacherOnlyTests
{
    /// <summary>Нестандартный сид-пароль кейса (задан в given).</summary>
    private const string OwnTeacherPassword = "Str0ng!Unique2026";

    [Fact]
    public async Task ProductionWithOwnPassword_Starts_Login200_GroupsAndLabsEmpty()
    {
        // given: Production-хост с валидным Auth__JwtKey и своим Seed__TeacherPassword.
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

        // then: приложение стартует; вход — 200.
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var accessToken = HostClients.ExtractAccessToken(login);
        Assert.False(string.IsNullOrEmpty(accessToken), "Вход не выпустил access_token cookie.");

        // when: инспекция хранилищ пользователей/групп/работ/сдач.
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var labs = factory.Services.GetRequiredService<ILabRepository>();
        var submissions = factory.Services.GetRequiredService<ISubmissionRepository>();

        // then: создан только преподаватель — студентов нет; группы/работы/сдачи пусты.
        Assert.NotNull(users.GetByLogin(SeedOptions.DefaultTeacherLogin));
        Assert.Empty(users.ListStudents());
        Assert.Empty(groups.GetAll());
        var storedLabs = labs.GetAll();
        Assert.Empty(storedLabs);
        Assert.Empty(submissions.ListByLabIds(storedLabs.Select(lab => lab.Id).ToList()));

        // then (контрольный срез через API): GET /groups и GET /labs под teacher — 200, пусто.
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
