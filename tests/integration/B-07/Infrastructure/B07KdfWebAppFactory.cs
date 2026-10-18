using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Фикстура KDF-сценария батча B-07 (TS-025; FR-005): механика
/// <see cref="B07WebAppFactory"/> (Development) с тестовым значением итераций
/// Auth__Pbkdf2Iterations=1000 — given кейса «IPasswordHasher с
/// Auth__Pbkdf2Iterations=1000 (тестовое значение)»; формат хэша
/// «pbkdf2-sha256$1000$…» проверяется дословно по префиксу.
/// </summary>
public sealed class B07KdfWebAppFactory : B07WebAppFactory
{
    /// <summary>Тестовое число итераций PBKDF2 (FR-005: тесты задают меньшее).</summary>
    public const int TestPbkdf2Iterations = 1000;

    public B07KdfWebAppFactory()
        : base(
            Environments.Development,
            new Dictionary<string, string?>
            {
                [AuthOptions.Pbkdf2IterationsVariable] = TestPbkdf2Iterations.ToString(),
            })
    {
    }
}
