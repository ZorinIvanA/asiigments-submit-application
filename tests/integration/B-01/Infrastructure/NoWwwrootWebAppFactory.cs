using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B01.Infrastructure;

/// <summary>
/// Тестовый хост с ОТСУТСТВУЮЩИМ каталогом wwwroot (кейс TS-010, AC «Нет index.html»
/// FR-002): content root переносится в чистый временный каталог — ни wwwroot, ни
/// appsettings.json там нет, конфигурация складывается из умолчаний окружения
/// (Development — валидна без секретов, FR-008), webroot-каталог хостом не создаётся.
/// Временный каталог удаляется при освобождении фабрики.
/// </summary>
public sealed class NoWwwrootWebAppFactory : B01WebAppFactory
{
    private readonly string _contentRoot;

    /// <summary>
    /// Development-хост без wwwroot; settings переопределяют конфигурацию поверх
    /// умолчаний окружения (как в <see cref="B01WebAppFactory"/>, поздние значения выигрывают).
    /// </summary>
    public NoWwwrootWebAppFactory(IReadOnlyDictionary<string, string?>? settings = null)
        : base(Environments.Development, settings)
    {
        _contentRoot = Directory.CreateTempSubdirectory("b01-no-wwwroot-").FullName;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Применяется ПОСЛЕ base-настроек: content root переносится в пустой каталог.
        builder.UseSetting(WebHostDefaults.ContentRootKey, _contentRoot);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Временный каталог — расходный материал: невозможность удалить его не
                // меняет результат теста и не должна маскировать исключение сценария.
            }
        }

        base.Dispose(disposing);
    }
}
