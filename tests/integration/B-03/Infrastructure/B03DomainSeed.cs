using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// DI-сид работ кейсов списков B-03 (TS-088..TS-091, FR-017): прямое наполнение
/// ILabRepository из factory.Services, минуя публичный API (данные given «сид
/// развёрнут: 20 работ семестра 1 и 3 работы семестра 2» — детерминированный состав
/// при Seed__DemoData=false). Идемпотентен: существующие пары пропускаются.
/// </summary>
public static class B03DomainSeed
{
    /// <summary>
    /// Работа заданной пары (semester, number): добавляется в ILabRepository, если пара
    /// свободна; иначе возвращается существующая запись (идемпотентность сида).
    /// </summary>
    public static Lab AddLab(WebApplicationFactory<Program> factory, int semester, int number)
    {
        var labs = factory.Services.GetRequiredService<ILabRepository>();
        if (labs.TryGetByPair(semester, number) is { } existing)
        {
            return existing;
        }

        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = semester,
            Number = number,
            Content = $"Содержание DI-сида работы {semester}:{number}",
            AssignmentUrl = null,
            DefenseRequired = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        labs.Add(lab);
        return lab;
    }

    /// <summary>Набор работ заданных пар (semester, number) — данные given кейсов списков.</summary>
    public static void AddLabs(WebApplicationFactory<Program> factory, params (int Semester, int Number)[] pairs)
    {
        foreach (var (semester, number) in pairs)
        {
            AddLab(factory, semester, number);
        }
    }

    /// <summary>
    /// Состав given TS-088..TS-091: 20 работ семестра 1 (номера 1..20)
    /// и 3 работы семестра 2 (номера 1..3) — итого 23 записи.
    /// </summary>
    public static void AddStandard23Labs(WebApplicationFactory<Program> factory)
    {
        var pairs = new List<(int Semester, int Number)>(23);
        pairs.AddRange(Enumerable.Range(1, 20).Select(number => (1, number)));
        pairs.AddRange(Enumerable.Range(1, 3).Select(number => (2, number)));
        AddLabs(factory, [.. pairs]);
    }
}
