using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Фикстура кейса TS-200 (given «демо-сид», FR-025): механика
/// <see cref="B07WebAppFactory"/> (Development) с Seed__DemoData=true —
/// демонстрационный набор сидa: 3 группы (ИК-221/222/223), студенты
/// student01..student32 (01–25 в ИК-221), 23 работы (семестр 1 №1–20,
/// семестр 2 №1–3), 4 сид-сдачи. Значение переменной передаётся через
/// внутренний конструктор базовой фикстуры: словарь _settings применяется
/// в ConfigureWebHost ПОСЛЕ явного «false» — позднее UseSetting того же
/// ключа выигрывает (механика производных фикстур зоны, см.
/// <see cref="B07KdfWebAppFactory"/>).
/// </summary>
public sealed class B07DemoSeedWebAppFactory : B07WebAppFactory
{
    public B07DemoSeedWebAppFactory()
        : base(
            Environments.Development,
            new Dictionary<string, string?>
            {
                [SeedOptions.DemoDataVariable] = "true",
            })
    {
    }
}
