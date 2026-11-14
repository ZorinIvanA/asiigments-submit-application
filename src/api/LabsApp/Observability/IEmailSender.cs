namespace LabsApp.Observability;

/// <summary>
/// Заглушка доставки email (IF-005): «доставка» выполняется журналированием,
/// SMTP вне области (OQ-001). Реализации: <see cref="DevEmailSender"/>
/// (Development — категория «EmailDev» с маркером [DEV-EMAIL], единственное место
/// журнала, где допустим код восстановления, NFR-006/ISS-003) и
/// <see cref="ProductionEmailSender"/> (среды ≠ Development — no-op с ОДНИМ
/// warning без адресата и содержимого письма). Выбор реализации по IHostEnvironment
/// выполняется один раз в композиция-корне (Program.cs); потребители зависят
/// только от интерфейса.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Доставляет письмо (FR-012): Development — запись категории «EmailDev»
    /// с маркером [DEV-EMAIL], адресатом и текстом письма (включая код
    /// восстановления); формат записи одинаков для существующего и
    /// несуществующего адресата — журнал не раскрывает существование учётной
    /// записи. Среды ≠ Development — no-op с ОДНИМ warning без адресата и
    /// содержимого (NFR-006: код в журнал не попадает вовсе).
    /// </summary>
    Task SendAsync(string toEmail, string subject, string body);
}
