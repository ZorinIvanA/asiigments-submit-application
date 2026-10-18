using System.Diagnostics;
using LabsApp.IntegrationTests.B18.Infrastructure;

namespace LabsApp.IntegrationTests.B18.Scenarios;

/// <summary>
/// Кейс TS-146 (текущая нумерация раунда; историческая нумерация — TS-181),
/// инфраструктура хелпера: проверка свойств DotNetCli, требуемых
/// given/then кейса (переиздан a-034):
/// - CR-003: глобальный межпроцессный замок с единым зафиксированным именем
///   (lock-файл SrcApiBuildLock.LockFilePath) удерживается всё время жизни
///   дочернего процесса — параллельные dotnet build/test ДЕРЖАТЕЛЕЙ замка
///   B-20/B-22 над тем же src/api не пересекаются на obj/ и не дают
///   ложного красного (B-16 сериализуется только xUnit-коллекцией);
///   ПОПРАВКА раунда доработки CR-001: зона B-21 (Ts185_BuildGatesTests,
///   B21ProcessRunner) исполняет dotnet build/test над src/api БЕЗ захвата
///   замка — некоординированный участник; от пересечения с ней замок не
///   защищает (сериализация таких зон — вопрос диспетчеризации прогона);
/// - CR-004: таймаут диагностируется возвращаемым результатом (TimedOut=true
///   + накопленный хвост вывода) без необработанного исключения из чтения
///   stdout/stderr (Task&lt;T&gt;.Result — только при RanToCompletion);
///   ДОРАБОТКА CR-001/CR-004: бюджет ожидания замка отделён от бюджета
///   дочернего процесса (GenerousLockWait заведомо больше самой долгой
///   удерживаемой команды — полного dotnet test над src/api).
/// </summary>
[Collection("b18-backend-dotnet-cli")]
public sealed class Ts181_DotNetCliInfrastructureTests
{
    /// <summary>CR-004: таймаут — это результат TimedOut=true, а не исключение.</summary>
    [Fact]
    public void DotNetCli_Timeout_DiagnosedAsTimedOutResult_WithoutException()
    {
        // dotnet --version занимает на хосте прогона ~100 мс (замер) — за
        // таймаут 1 мс завершиться не может, снятие детерминировано; ошибки
        // чтения stdout/stderr не должны выплеснуться исключением.
        var result = DotNetCli.Run(["--version"], TimeSpan.FromMilliseconds(1));

        Assert.True(
            result.TimedOut,
            $"Ожидался TimedOut=true при таймауте 1 мс; фактически: exit={result.ExitCode}, "
            + $"timedOut={result.TimedOut}.");
    }

    /// <summary>
    /// CR-003 (а): пока жив дочерний dotnet, внешний процесс обязан видеть
    /// lock-файл занятым (эксклюзивный захват отвергается) — замок удерживается
    /// всё время жизни дочернего процесса и лежит по фиксированному пути.
    /// Дочерняя команда — сборка LabsApp.Tests (секунды даже инкрементально):
    /// мгновенные команды (—list-sdks) завершаются раньше первого опроса.
    /// </summary>
    [Fact]
    public async Task DotNetCli_HoldsGlobalInterprocessLock_DuringChildLifetime()
    {
        var run = Task.Run(() => DotNetCli.Run(
            ["build", BackendSuitePaths.TestsProjectFile, "--nologo", "-v", "minimal"],
            DotNetCli.GenerousLockWait,
            TimeSpan.FromMinutes(15)));

        var lockBusyObserved = false;
        var stopwatch = Stopwatch.StartNew();
        while (!run.IsCompleted && stopwatch.Elapsed < TimeSpan.FromMinutes(2))
        {
            try
            {
                using var probe = new FileStream(
                    SrcApiBuildLock.LockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException)
            {
                lockBusyObserved = true;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(10));
        }

        var result = await run;

        Assert.True(
            result.Succeeded,
            $"Дочерний dotnet (build LabsApp.Tests) должен успешно выполниться под замком."
                + $"{Environment.NewLine}{result.OutputTail()}");
        Assert.True(
            lockBusyObserved,
            "CR-003 нарушен: пока жил дочерний dotnet, внешний эксклюзивный захват lock-файла "
            + "ни разу не был отвергнут — замок не удерживается всё время жизни дочернего процесса.");
        Assert.True(
            File.Exists(SrcApiBuildLock.LockFilePath),
            "Lock-файл не создан по единому зафиксированному пути репозитория: "
            + SrcApiBuildLock.LockFilePath);
    }

    /// <summary>
    /// CR-003 (б): замок, удерживаемый внешним процессом, блокирует запуск
    /// дочернего dotnet (диагностика TimedOut с упоминанием замка); после
    /// отпускания замка прогон проходит успешно. Внешний захват идёт через
    /// сам <see cref="SrcApiBuildLock.TryAcquire"/> (как у производственного
    /// пути DotNetCli): в дереве полного прогона замок в момент старта теста
    /// может быть занят параллельной зоной-соучастником замка (B-20/B-22) — сырой
    /// FileStream(FileShare.None) в таком случае падал бы IOException вместо
    /// исполнения сценария; TryAcquire ограниченно дожидается освобождения.
    /// </summary>
    [Fact]
    public void DotNetCli_GlobalLockHeldExternally_BlocksChild_AndReleaseResumes()
    {
        var externalStream = SrcApiBuildLock.TryAcquire(TimeSpan.FromMinutes(10));
        Assert.True(
            externalStream is not null,
            "Не удалось захватить внешний замок (SrcApiBuildLock.TryAcquire) за 10 минут: "
            + "сценарий «замок удержан внешним процессом» невыполним — замок всё время занят "
            + "чужим держателем " + SrcApiBuildLock.LockFilePath);

        DotNetCliResult blocked;
        try
        {
            blocked = DotNetCli.Run(["--list-sdks"], TimeSpan.FromSeconds(5));
        }
        finally
        {
            // Отпустить замок ДО фазы «после отпускания»: handle обязан жить
            // ровно сценарий блокировки, не дольше (иначе resumed-прогон ждёт
            // замок, удерживаемый самим тестом).
            externalStream.Dispose();
        }

        Assert.True(
            blocked.TimedOut,
            "Пока замок удержан внешним процессом, дочерний dotnet не должен был запуститься "
            + "(ожидался TimedOut=true).");
        Assert.Contains("замок", blocked.Output, StringComparison.OrdinalIgnoreCase);

        // Доработка CR-001/CR-004: бюджеты РАЗДЕЛЕНЫ. Ожидание освобождения
        // замка — GenerousLockWait (заведомо дольше самой долгой удерживаемой
        // команды — полного dotnet test над src/api, бюджет Ts182 — 30 мин),
        // бюджет самого дочернего процесса (--list-sdks, ~1 с) остаётся малым:
        // «не взяли замок» и «дочерний процесс не успел» классифицируются
        // раздельно, ожидание чужого держателя больше не съедает детский
        // бюджет (прежний единый таймаут 3 мин давал ложный красный
        // в параллельном полном прогоне).
        var resumed = DotNetCli.Run(
            ["--list-sdks"],
            DotNetCli.GenerousLockWait,
            TimeSpan.FromMinutes(3));
        Assert.True(
            resumed.Succeeded,
            $"После отпускания замка прогон обязан завершиться успешно."
                + $"{Environment.NewLine}{resumed.OutputTail()}");
    }
}
