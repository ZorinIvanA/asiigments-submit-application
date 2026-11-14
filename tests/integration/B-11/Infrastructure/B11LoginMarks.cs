using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>
/// Сев меток login-лимитера поведенчески (кейс-закон FR-004: «метка пишется
/// только учтённой неудачной попыткой»): count неудачных POST /auth/login при
/// фиктивном времени стенда (FR-003 — инжектируемые часы). Возвращает моменты
/// меток (время перед каждым запросом — движок штампует метку теми же часами
/// без продвижения внутри запроса). Используется кейсами TS-038..TS-042/TS-046:
/// шаги given «N меток ключа в текущем окне» и проверки «число меток не
/// изменилось» без интроспекции хранилища лимитера.
/// </summary>
public static class B11LoginMarks
{
    /// <summary>Одна неудачная попытка входа в текущее время часов; возвращает её момент.</summary>
    public static async Task<DateTimeOffset> FailOnceAsync(
        FakeTimeProvider time,
        HttpClient client,
        string login,
        string password)
    {
        var at = time.GetUtcNow();
        using var response = await HostClients.LoginAsync(client, login, password);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return at;
    }

    /// <summary>
    /// Несколько неудачных попыток с шагом step между ними (null — без продвижения
    /// часов, метки в один момент); возвращает моменты меток по возрастанию.
    /// </summary>
    public static async Task<IReadOnlyList<DateTimeOffset>> FailManyAsync(
        FakeTimeProvider time,
        HttpClient client,
        string login,
        string password,
        int count,
        TimeSpan? step = null)
    {
        var marks = new List<DateTimeOffset>(count);
        for (var i = 0; i < count; i++)
        {
            if (i > 0 && step.HasValue)
            {
                time.Advance(step.Value);
            }

            marks.Add(await FailOnceAsync(time, client, login, password));
        }

        return marks;
    }
}
