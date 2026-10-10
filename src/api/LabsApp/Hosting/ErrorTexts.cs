namespace LabsApp.Hosting;

/// <summary>
/// Тексты конвейерных ответов хостинга (IF-001/FR-023, глоссарий v2.2 — дословно).
/// Полевой словарь ошибок живёт в Domain.Validation.ErrorTexts (зона C-002); здесь —
/// только тексты, которыми отвечает сам конвейер хостинга: 404 несопоставленного
/// маршрута под /api и /swagger* вне Development, 500 необработанного исключения.
/// Инфраструктурные отклонения Kestrel ДО конвейера (413 сверх лимита тела, 400 на
/// невалидном HTTP) отвечают телом фреймворка и конверту не подчиняются
/// (ISS-016/OQ-004) — текста для них в словаре нет.
/// </summary>
public static class ErrorTexts
{
    /// <summary>Текст 400: ошибки валидации/привязки данных (§8).</summary>
    public const string InvalidData = "Данные заполнены неверно";

    /// <summary>
    /// Текст 404: несопоставленный маршрут под /api (любой метод) и /swagger*
    /// вне Development (FR-001/FR-002/FR-023).
    /// </summary>
    public const string NotFound = "Не найдено";

    /// <summary>Текст 500: необработанное исключение — без деталей и stack trace (FR-023).</summary>
    public const string InternalServerError = "Внутренняя ошибка сервера";
}
