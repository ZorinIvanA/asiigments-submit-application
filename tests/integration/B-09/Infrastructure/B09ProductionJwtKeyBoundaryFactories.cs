using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Production-фикстуры границы длины Auth__JwtKey (кейс TS-194, FR-008:
/// «ключ Auth__JwtKey ≥32 байт»; граница 31/32 байта): (а) строка ровно
/// 31 ASCII-символ — отказ старта; (б) строка ровно 32 ASCII-символа —
/// старт успешен. В обеих Seed__TeacherPassword задан НЕстандартный
/// (валидный), чтобы причина отказа/успеха была сконцентрирована ТОЛЬКО
/// в длине ключа. ASCII-строки: 1 символ = 1 байт, длина в байтах равна
/// string.Length. Новый файл текущей волны батча: производные от
/// <see cref="B09AuthWebAppFactory"/> (protected-конструктор окружение +
/// настройки); существующая инфраструктура зоны не изменяется.
/// </summary>
public sealed class B09ProductionJwtKey31BytesFactory : B09AuthWebAppFactory
{
    /// <summary>Ровно 31 ASCII-байт — ниже границы MUST FR-008 (16 + 15 символов).</summary>
    public const string JwtKey31Bytes = "0123456789abcdef0123456789abcde";

    public B09ProductionJwtKey31BytesFactory()
        : base(
            Environments.Production,
            new Dictionary<string, string?>
            {
                [AuthOptions.JwtKeyVariable] = JwtKey31Bytes,
                [SeedOptions.TeacherPasswordVariable] = B09AuthWebAppFactory.ProductionTeacherPassword,
            })
    {
    }
}

/// <summary>
/// Production-фикстура НИЖНЕЙ границы: строка ровно 32 ASCII-байта
/// (HS256 требует ключ ≥256 бит) — старт обязан быть успешным (TS-194(б)).
/// </summary>
public sealed class B09ProductionJwtKey32BytesFactory : B09AuthWebAppFactory
{
    /// <summary>Ровно 32 ASCII-байта — нижняя граница MUST FR-008 (16 + 16 символов).</summary>
    public const string JwtKey32Bytes = "0123456789abcdef0123456789abcdef";

    public B09ProductionJwtKey32BytesFactory()
        : base(
            Environments.Production,
            new Dictionary<string, string?>
            {
                [AuthOptions.JwtKeyVariable] = JwtKey32Bytes,
                [SeedOptions.TeacherPasswordVariable] = B09AuthWebAppFactory.ProductionTeacherPassword,
            })
    {
    }
}
