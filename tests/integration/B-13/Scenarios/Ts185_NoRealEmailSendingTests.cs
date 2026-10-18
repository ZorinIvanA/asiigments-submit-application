namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-185 (P2, scope; OUT-SCOPE-SMTP, FR-012) «Scope: реальная отправка почты
/// отсутствует».
/// given: Зона src/api (без bin/obj).
/// when:  Поиск 'SmtpClient', 'System.Net.Mail', 'MailKit' по src/api.
/// then:  0 вхождений — только IEmailSender-заглушка (out_of_scope: «Реальная
///        отправка почты (SMTP)»; поведение заглушки проверено TS-069..TS-071).
///
/// Корпус: код и файлы проекта src/api (без каталогов bin/obj). Просматриваются
/// текстовые файлы кода и проекта (.cs/.csproj/.sln/.props/.targets/.json) —
/// SMTP-код (System.Net.Mail.SmtpClient) или пакет (MailKit) могли бы появиться
/// только в них; корпус litter-файлов (.trx/.md/.ico/…) маркизу не затрагивает
/// (образец — корпусный кейс Ts161 зоны B-07). «Только IEmailSender-заглушка»
/// проверяется составом реализаций IEmailSender в сборке приложения: ровно
/// dev- и prod-заглушки (IF-005), никакой транспортной реализации.
/// </summary>
public sealed class Ts185_NoRealEmailSendingTests
{
    private static readonly string[] SmtpMarkers =
    [
        "SmtpClient",
        "System.Net.Mail",
        "MailKit",
    ];

    private static readonly string[] ScannedExtensions =
    [
        ".cs",
        ".csproj",
        ".sln",
        ".props",
        ".targets",
        ".json",
    ];

    [Fact]
    public void SmtpMarkers_HaveZeroOccurrencesInSrcApi_OnlyEmailSenderStubExists()
    {
        // given: зона src/api (без каталогов bin/obj).
        var srcApiRoot = LocateSrcApiRoot();
        var corpus = Directory
            .EnumerateFiles(srcApiRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
                    && !segments.Contains("obj", StringComparer.OrdinalIgnoreCase)
                    && ScannedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Санит-стража корпуса: файл проекта LabsApp.csproj обязан быть в выборке —
        // иначе пустой корпус дал бы ложно-зелёный результат.
        Assert.Contains(
            corpus,
            path => path.EndsWith(
                $"LabsApp{Path.DirectorySeparatorChar}LabsApp.csproj", StringComparison.Ordinal));

        // when: поиск 'SmtpClient', 'System.Net.Mail', 'MailKit' по src/api.
        var hits = new List<string>();
        foreach (var path in corpus)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                // Нечитаемый в момент прогона файл пропускается (блокировка внешним
                // процессом) — корпус кода и файлов проекта от этого не пустеет.
                continue;
            }

            var lines = text.Split('\n');
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                foreach (var marker in SmtpMarkers)
                {
                    if (lines[lineIndex].Contains(marker, StringComparison.Ordinal))
                    {
                        hits.Add($"{path}:{lineIndex + 1} [{marker}]");
                    }
                }
            }
        }

        // then: 0 вхождений.
        Assert.True(
            hits.Count == 0,
            "Ожидалось 0 вхождений SMTP-маркеров ('SmtpClient', 'System.Net.Mail', " +
            "'MailKit') в src/api, найдены: " + string.Join(", ", hits));

        // then: только IEmailSender-заглушка — реализаций IEmailSender в сборке
        // приложения ровно две, обе заглушки (IF-005): dev-журналирование и no-op.
        var emailSenderType = typeof(LabsApp.Observability.IEmailSender);
        var implementors = emailSenderType.Assembly.GetTypes()
            .Where(type => type.IsClass
                && !type.IsAbstract
                && emailSenderType.IsAssignableFrom(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "DevEmailSender", "ProductionEmailSender" }, implementors);
    }

    /// <summary>
    /// Поднимается от выходного каталога тестовой сборки до корня репозитория
    /// (каталог, содержащий src/api/LabsApp/LabsApp.csproj).
    /// </summary>
    private static string LocateSrcApiRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; candidate is not null && depth < 12; depth++)
        {
            var probe = Path.Combine(candidate.FullName, "src", "api", "LabsApp", "LabsApp.csproj");
            if (File.Exists(probe))
            {
                return Path.Combine(candidate.FullName, "src", "api");
            }

            candidate = candidate.Parent;
        }

        throw new InvalidOperationException(
            $"Корень репозитория с src/api/LabsApp/LabsApp.csproj не найден от {AppContext.BaseDirectory}.");
    }
}
