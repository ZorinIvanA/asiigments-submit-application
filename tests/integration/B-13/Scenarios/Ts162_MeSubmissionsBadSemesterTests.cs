using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-162 (P1, boundary; FR-021/IF-001, v2.2 ASM-018) «GET /me/submissions:
/// отсутствующий/нецелочисленный semester — строгий 400 с errors.semester».
/// given: Студент в группе с работами и сдачами.
/// when: GET /api/v1/me/submissions?semester=abc; отдельно без параметра semester.
/// then: Оба → 400 «Данные заполнены неверно» c errors.semester «Семестр — число
///       от 1 до 10» (асимметрия ASM-018: /me/submissions — строгая валидация
///       semester, в отличие от мягкой нормализации GET /labs).
/// </summary>
public sealed class Ts162_MeSubmissionsBadSemesterTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS162_MeSubmissions_NonIntegerSemester_ReturnsEmptyLabsAndSubmissions()
    {
        // given: студент в группе с работой и своей сдачей в семестре 1; сессия студента.
        var login = "ts162-abc";
        SeedStudentWithGroupWorkAndSubmission(login, semester: 1, number: 1);
        using var client = HostClients.CreateStudentClient(_factory, login);

        // when: GET /me/submissions?semester=abc.
        using var response = await client.GetAsync("/api/v1/me/submissions?semester=abc");

        // then: 400 «Данные заполнены неверно» c errors.semester
        //       «Семестр — число от 1 до 10» (строгая валидация ASM-018).
        await AssertSemesterFieldErrorAsync(response);
    }

    [Fact]
    public async Task TS162_MeSubmissions_MissingSemester_Returns400WithSemesterFieldError()
    {
        // given: студент в группе с работой и своей сдачей в семестре 1; сессия студента.
        var login = "ts162-missing";
        SeedStudentWithGroupWorkAndSubmission(login, semester: 1, number: 2);
        using var client = HostClients.CreateStudentClient(_factory, login);

        // when: GET /me/submissions (без параметра semester).
        using var response = await client.GetAsync("/api/v1/me/submissions");

        // then: 400 «Данные заполнены неверно» c errors.semester
        //       «Семестр — число от 1 до 10» (строгая валидация ASM-018).
        await AssertSemesterFieldErrorAsync(response);
    }

    /// <summary>then кейсов TS-162: 400 + errors.semester «Семестр — число от 1 до 10»
    /// (конверт полевой валидации IF-001: message + errors ровно по одному полю).</summary>
    private static async Task AssertSemesterFieldErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal("Данные заполнены неверно", body.GetProperty("message").GetString());
        var semesterErrors = body.GetProperty("errors").GetProperty("semester");
        Assert.Equal(1, semesterErrors.GetArrayLength());
        Assert.Equal("Семестр — число от 1 до 10", semesterErrors[0].GetString());
    }

    /// <summary>Шаг given «студент в группе с работами и сдачами» (уникальные логины —
    /// общее хранилище на класс-фикстуру). Пара (semester, number) работы уникальна
    /// на каждый тест-метод: индекс пары «{semester}:{number}» глобален в общем
    /// ILabRepository класс-фикстуры — повторная пара даёт StorageConflictException
    /// (CR-001). Проверяемое поведение от пары не зависит: semester=abc/отсутствует
    /// → labs=[] и submissions=[] при любой корректной работе с сдачей студента.</summary>
    private void SeedStudentWithGroupWorkAndSubmission(string login, int semester, int number)
    {
        var group = B13Seed.AddGroup(_factory, $"Группа TS-162 {login}");
        var student = B13Seed.AddStudent(_factory, login, "Алексеев", group.Id);
        var lab = B13Seed.AddLab(_factory, semester, number);
        B13Seed.AddSubmission(_factory, student.Id, lab.Id, "2025-05-01", null);
    }
}
