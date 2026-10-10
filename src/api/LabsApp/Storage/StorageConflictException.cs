namespace LabsApp.Storage;

/// <summary>
/// Нарушение уникальности хранилища при составной операции (FR-002): занятый
/// ci-логин/ci-email, занятое имя группы, занятая пара (семестр, номер) работы.
/// Сервис ловит этот тип и отображает в 409 (спецификация, таблица кодов).
/// </summary>
public sealed class StorageConflictException(string message) : Exception(message)
{
}
