namespace LabsApp.Hosting;

/// <summary>
/// Категории журнала hosting-части (реестр категорий v2.2, tech solution
/// observability.logging): warnings конфигурации — «Hosting.Configuration»
/// (эпизодический dev-ключ JWT, некорректное Seed__DemoData; та же категория
/// используется no-op IEmailSender вне Development — ProductionEmailSender.LogCategory).
/// Без префикса пространства имён — как «Api.Request»/«Api.Security»/«EmailDev».
/// </summary>
public static class HostingLogCategories
{
    /// <summary>Категория warnings конфигурации (реестр категорий v2.2).</summary>
    public const string Configuration = "Hosting.Configuration";
}
