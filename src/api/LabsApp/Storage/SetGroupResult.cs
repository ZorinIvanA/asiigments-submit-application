namespace LabsApp.Storage;

/// <summary>
/// Итог узкой атомарной мутации группы студента <see cref="IUserRepository.SetGroup"/>
/// (SEC-001): различает, какая из проверок не прошла, — вызывающая сторона
/// сопоставляет значения с разными текстами 404 (IF-011 «Студент не найден» /
/// «Группа не найдена»).
/// </summary>
public enum SetGroupResult
{
    /// <summary>Группа студента изменена: назначение/перевод либо снятие в null.</summary>
    Success,

    /// <summary>Пользователь не найден либо его роль ≠ student — 404 «Студент не найден».</summary>
    StudentNotFound,

    /// <summary>Целевая группа не существует (groupExists вернул false) — 404 «Группа не найдена».</summary>
    GroupNotFound,
}
