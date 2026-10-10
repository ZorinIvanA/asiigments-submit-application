using System.Diagnostics;

namespace LabsApp.IntegrationTests.B21.Infrastructure;

/// <summary>
/// Дочерние команды dotnet CLI ворот TS-185/TS-191 (NFR-002: `dotnet build` /
/// `dotnet test` над src/api): глобальный межпроцессный замок <see cref="B21SrcApiBuildLock"/>
/// (аменда a-124/CR-001 по образцу зон B-20/B-22) захватывается ДО старта
/// дочернего процесса и удерживается до его полного завершения — параллельные
/// dotnet build/test над тем же src/api из других тестовых зон (B-18/B-20/B-22)
/// не пересекаются на obj/. Замок не захвачен — дочерний процесс не
/// запускается, возвращается диагностический результат с кодом -1 (ворота
/// честно падают с причиной). Окружение —
/// <see cref="B21ProcessRunner.ApplyCommonEnvironment"/>.
/// </summary>
public static class B21DotNetCli
{
    /// <summary>Ожидание освобождения замка — на уровне тайм-аута самой команды.</summary>
    public static async Task<B21ProcessResult> RunAsync(
        IReadOnlyList<string> arguments, int timeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var lockHandle = B21SrcApiBuildLock.TryAcquire(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        if (lockHandle is null)
        {
            return new B21ProcessResult(
                ExitCode: -1,
                Output: "Не удалось захватить глобальный межпроцессный замок «"
                    + B21SrcApiBuildLock.LockFilePath + "» за "
                    + TimeSpan.FromMilliseconds(timeoutMilliseconds).TotalSeconds.ToString("F0")
                    + " с: в другой тестовой зоне выполняется dotnet build/test над src/api "
                    + "(дочерний процесс не запускался, пересечения сборок на obj/ нет).");
        }

        using (lockHandle)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ResolveDotNetMuxer(),
                WorkingDirectory = B21RepoPaths.RepositoryRoot,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            B21ProcessRunner.ApplyCommonEnvironment(startInfo.Environment);
            return await B21ProcessRunner.RunAsync(startInfo, timeoutMilliseconds);
        }
    }

    /// <summary>
    /// Путь к muxer'у .NET, под которым дочерние dotnet-команды идут ПОЛНОСТЬЮ
    /// на установке с рантаймом 8.x (пользовательская установка ~/.dotnet):
    /// системный muxer (SDK 10, рантаймы 9/10) прокатывает тест-хост net8→net10
    /// через DOTNET_ROLL_FORWARD, и AspNetCore.App 10 даёт иное поведение
    /// эндпойнтов приложения (ложные 500 на зелёном на net8 наборе). Установки
    /// с 8.x нет — системный «dotnet» (поведение прежних прогонов).
    /// </summary>
    private static string ResolveDotNetMuxer()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
        {
            return "dotnet";
        }

        var userRoot = Path.Combine(home, ".dotnet");
        var userMuxer = Path.Combine(userRoot, "dotnet");
        if (!File.Exists(userMuxer))
        {
            return "dotnet";
        }

        var coreAppDirectory = Path.Combine(userRoot, "shared", "Microsoft.NETCore.App");
        return Directory.Exists(coreAppDirectory)
            && Directory.GetDirectories(coreAppDirectory, "8.*", SearchOption.TopDirectoryOnly).Length > 0
                ? userMuxer
                : "dotnet";
    }
}
