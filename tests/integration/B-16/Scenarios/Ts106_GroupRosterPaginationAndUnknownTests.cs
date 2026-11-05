using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;
using LabsApp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-106 «groups: состав с пагинацией и ru-порядком (дискриминирующие ФИО);
/// неизвестная группа — 404» (happy_path, FR-019, P0; РЕВ-ISS-003, AR-005).
///
/// given: в группе ИК-221 25 сид-студентов (демо-сид student01..student25,
///        fullName «Иванов Иван Иванович NN»); в ИК-221 дополнительно включены
///        два студента с ФИО, дающими РАЗНЫЙ порядок при ru-культуре и
///        ординальном сравнении: «Авроров Иван» (login avrorov) и «Ёлкин Пётр»
///        (login elkin) — созданы ПРЯМЫМ DI-сидом IUserRepository тестового хоста
///        (аутентификации в приложении нет: POST /auth/register удалён; пароль
///        не используется — password_hash плейсхолдер «!») и включены в группу
///        запросом PUT /students/{id}/group {groupId:&lt;ИК-221&gt;}; итого 27
///        студентов. При ru: Авроров &lt; Ёлкин &lt; Иванов…; ординал-нижний:
///        ё (U+0451) после и (U+0438) — Ёлкин после Ивановых; ординал-верхний:
///        Ё (U+0401) до А — Ёлкин первой (данные фальсифицируют неверную
///        культуру сравнения).
/// when:  GET /groups/{id ИК-221}/students?page=1;
///        GET /groups/{id ИК-221}/students?page=3;
///        GET /groups/&lt;несуществующий-uuid&gt;/students.
/// then:  page=1 — 200 {items:10, total:27, page:1, pageSize:10}; все items —
///        StudentDto с groupId=id группы и groupName='ИК-221'; порядок fullName↑
///        затем login↑ по правилам русской локали (ru): первые два элемента —
///        «Авроров Иван», «Ёлкин Пётр», далее «Иванов Иван Иванович 01»…«08» —
///        иной порядок (Ёлкин первой или после Ивановых) — провал; page=3 — 200
///        {items:7, total:27, page:3, pageSize:10} (хвост «Иванов Иван Иванович
///        19»…«25» — пагинация сквозная по полной выборке); несуществующая
///        группа — 404 «Группа не найдена» (FR-019 AC «Состав с пагинацией»,
///        «Неизвестная группа»; AR-005).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts106_GroupRosterPaginationAndUnknownTests : IClassFixture<B16GroupsDemoDataFactory>
{
    private const string NotFoundText = "Группа не найдена";

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts106_GroupRosterPaginationAndUnknownTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRoster_PagesAndRuOrder_UnknownGroupNotFound()
    {
        // given: анонимный клиент; id группы ИК-221 (25 сид-студентов демо-сида).
        using var client = B16GroupsSession.CreateClient(_factory);
        var ik221Id = await B16GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // given: «Авроров Иван» (avrorov) и «Ёлкин Пётр» (elkin) созданы прямым
        // DI-сидом IUserRepository тестового хоста.
        var avrorovId = await SeedStudentIdAsync("Авроров Иван", "avrorov", "avrorov@example.com");
        var elkinId = await SeedStudentIdAsync("Ёлкин Пётр", "elkin", "elkin@example.com");

        // given: оба включены в ИК-221 через PUT /students/{id}/group.
        using var attachAvrorov = await B16GroupsApi.PutStudentGroupAsync(client, avrorovId, ik221Id);
        Assert.True(
            attachAvrorov.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался HTTP 204 на PUT /students/{{avrorov}}/group, фактически {(int)attachAvrorov.StatusCode}: " +
            await attachAvrorov.Content.ReadAsStringAsync());
        using var attachElkin = await B16GroupsApi.PutStudentGroupAsync(client, elkinId, ik221Id);
        Assert.True(
            attachElkin.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался HTTP 204 на PUT /students/{{elkin}}/group, фактически {(int)attachElkin.StatusCode}: " +
            await attachElkin.Content.ReadAsStringAsync());

        // when: GET /groups/{id}/students?page=1.
        using var page1 = await B16GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=1");

        // then: 200 {items:10, total:27, page:1, pageSize:10}.
        using var page1Body = await B16GroupsApi.ParseWithStatusAsync(
            page1, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=1");
        var page1Items = page1Body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(27, B16GroupsApi.ReadInt(page1Body.RootElement, "total"));
        Assert.Equal(1, B16GroupsApi.ReadInt(page1Body.RootElement, "page"));
        Assert.Equal(10, B16GroupsApi.ReadInt(page1Body.RootElement, "pageSize"));
        Assert.True(
            page1Items.Count == 10,
            $"Ожидалось 10 items на странице 1 из 27 студентов, фактически {page1Items.Count}: " +
            $"{page1Body.RootElement.GetRawText()}");

        // then: все items — StudentDto с groupId=id группы и groupName='ИК-221';
        // состав {id, fullName, login, email, groupId, groupName}.
        foreach (var item in page1Items)
        {
            Assert.Equal(ik221Id, B16GroupsApi.ReadString(item, "groupId"));
            Assert.Equal("ИК-221", B16GroupsApi.ReadString(item, "groupName"));
            B16GroupsApi.ReadString(item, "id");
            B16GroupsApi.ReadString(item, "fullName");
            B16GroupsApi.ReadString(item, "login");
            B16GroupsApi.ReadString(item, "email");
        }

        // then: порядок fullName↑ затем login↑ (ru): «Авроров Иван», «Ёлкин
        // Пётр», далее «Иванов Иван Иванович 01»…«08».
        var page1FullNames = page1Items.Select(item => B16GroupsApi.ReadString(item, "fullName")).ToArray();
        var expectedPage1FullNames = new List<string>(10) { "Авроров Иван", "Ёлкин Пётр" };
        for (var nn = 1; nn <= 8; nn++)
        {
            expectedPage1FullNames.Add($"Иванов Иван Иванович {nn:00}");
        }

        Assert.True(
            expectedPage1FullNames.SequenceEqual(page1FullNames),
            $"Ожидался порядок состава страницы 1 [{string.Join(", ", expectedPage1FullNames)}] " +
            $"(fullName↑ затем login↑, русская локаль), фактически [{string.Join(", ", page1FullNames)}].");

        // when: GET /groups/{id}/students?page=3.
        using var page3 = await B16GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=3");

        // then: 200 {items:7, total:27, page:3, pageSize:10}; хвост
        // «Иванов Иван Иванович 19»…«25» (пагинация сквозная по полной выборке).
        using var page3Body = await B16GroupsApi.ParseWithStatusAsync(
            page3, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=3");
        var page3Items = page3Body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(27, B16GroupsApi.ReadInt(page3Body.RootElement, "total"));
        Assert.Equal(3, B16GroupsApi.ReadInt(page3Body.RootElement, "page"));
        Assert.Equal(10, B16GroupsApi.ReadInt(page3Body.RootElement, "pageSize"));
        Assert.True(
            page3Items.Count == 7,
            $"Ожидалось 7 items на странице 3 из 27 студентов, фактически {page3Items.Count}: " +
            $"{page3Body.RootElement.GetRawText()}");
        var page3FullNames = page3Items.Select(item => B16GroupsApi.ReadString(item, "fullName")).ToArray();
        var expectedPage3FullNames = Enumerable.Range(19, 7)
            .Select(nn => $"Иванов Иван Иванович {nn:00}")
            .ToArray();
        Assert.True(
            expectedPage3FullNames.SequenceEqual(page3FullNames),
            $"Ожидался хвост страницы 3 [{string.Join(", ", expectedPage3FullNames)}], " +
            $"фактически [{string.Join(", ", page3FullNames)}].");

        // when: GET /groups/<несуществующий-uuid>/students.
        var unknownId = Guid.NewGuid();
        using var unknown = await B16GroupsApi.GetGroupStudentsAsync(client, unknownId.ToString());

        // then: 404 'Группа не найдена'.
        using var unknownBody = await B16GroupsApi.ParseWithStatusAsync(
            unknown, HttpStatusCode.NotFound, $"GET /groups/{unknownId}/students");
        B16GroupsApi.MessageIs(unknownBody.RootElement, NotFoundText);
    }

    /// <summary>
    /// Шаг given TS-106: DI-сид студента в IUserRepository тестового хоста —
    /// uuid сид-записи возвращается вызывающему (аутентификации в приложении нет,
    /// пароль не используется: password_hash плейсхолдер «!», как в SeedRunner).
    /// </summary>
    private async Task<string> SeedStudentIdAsync(string fullName, string login, string email)
    {
        await _factory.Services.GetRequiredService<IUserRepository>().AddAsync(new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = "!",
            FullName = fullName,
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        });

        var user = await _factory.Services.GetRequiredService<IUserRepository>().GetByLoginAsync(login);
        Assert.True(
            user is not null,
            $"Сид-студент «{login}» не найден в DI-хранилище тестового хоста (шаг given неисполним).");
        return user!.Id.ToString();
    }
}
