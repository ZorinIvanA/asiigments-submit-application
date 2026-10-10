# Хэндофф конвейера pipeline (run 20261006T233834)

> Этот документ — инструкция для агента-диспетчера на **другом компьютере**,
> который продолжит незавершённый прогон мультиагентного конвейера
> (skill `pipeline-run`). Прогон **не финализирован** (статус: пауза,
> `final: null`) — это чистая пауза, а не фейл.

## 1. Что нужно знать сразу

- **Продукт**: репозиторий `asiigments-submit-application` (ASP.NET Core .NET 8 бэкенд `src/api` + Angular-клиент `src/client`), ветка `lab3-api`.
- **Прогон**: `run_id 20261006T233834`, рабочий каталог прогона — `.pipeline/run-lab3` (симлинк `.pipeline/latest` указывает на него). Фаза `loop3` (тестирование).
- **`.pipeline/` в .gitignore** — состояние прогона НЕ передаётся через git. Его нужно перенести вручную (см. §3).
- **Замороженная спека** продублирована в git: `docs/spec-frozen-20261006T233834.json`.

## 2. Установка движка на новом компьютере

1. Склонировать репозиторий движка: https://github.com/ZorinIvanA/multi-agent-pipeline (локально был по пути `/media/ivan/Software/Repos/multiagent pipeline/multi-agent-pipeline`).
2. Из каталога движка выполнить `python3 -m pipeline install` (создаёт агентов/команды и маркер `~/.zcode/pipeline-engine.path`), затем `python3 -m pipeline doctor`.
3. Все команды движка выполнять с cwd = каталог движка.

## 3. Перенос состояния прогона

Каталог `.pipeline/run-lab3` (~600 МБ, основной вес — `latest/` с артефактами, inputs/outputs и tmp-логами) перенести вручную любым способом вне git, например:

```bash
# на старой машине
cd <репозиторий продукта>
tar czf /tmp/pipeline-run-lab3.tar.gz .pipeline
# передать файл (scp/usb/облако), на новой машине:
cd <репозиторий продукта>
tar xzf /tmp/pipeline-run-lab3.tar.gz
ln -sfn run-lab3 .pipeline/latest   # если симлинк не сохранился
```

Минимально необходимое для resume: `state.json`, `journal.jsonl`, `inputs/`, `outputs/`, `artifacts/`, `arbitrations/`, `design-v12.json`. Каталог `tmp/` (логи и TRX) можно не переносить, но полезно.

## 4. Возобновление

В **новой сессии ZCode** (одна сессия — один прогон) вызвать skill `pipeline-run` с аргументом `resume`, либо напрямую:

```bash
cd <каталог движка>
python3 -m pipeline verify  --run-dir <репозиторий>/.pipeline/latest
python3 -m pipeline resume  --run-dir <репозиторий>/.pipeline/latest --repo-root <путь к репозиторию на новой машине>
python3 -m pipeline plan    --run-dir <репозиторий>/.pipeline/latest
```

Дальше диспетчер работает по циклу skill `pipeline-run` (launch → watch → arbitration/final).

## 5. Состояние на момент паузы (2026-10-10)

- **Бюджет**: вызовы 1448/2400 (лимит поднят с 1600 через `resume --raise-calls`), арбитражи 200, окно wall-clock свежее (последний resume ~2026-10-10 12:30; с того момента ~5,5 ч активного времени). При исчерпании wall-clock прогон финализируется как failed — продолжать через `resume` (опционально `--raise-wall 600`).
- **Очередь**: ~9 запланированных вызовов, включая `c-1349` (testcases v16), `c-1350..c-1352`, `c-1356`, `c-1358+` (ревью и прогоны последних батчей). Ничего не запущено.
- **Принято/закрыто**: батчи B-01..B-21 почти все в accept/run; тестовый код написан по всем зонам tests/integration/B-01..B-21; багфикс BUG-001 → T-002 выполнен и доработан.
- **Серия кейсов**: ~200 кейсов TS-001..TS-213, последняя ревизия — `testcases v16` (c-1349). Сценарное ревью прошло точки a-154/a-155 с продлениями +2/+2 — они **исчерпаны**; следующее упирание в iteration_limit будет предлагать только redo_scenarios/abort.
- **Известный риск**: серия scenario_review долгая (17+ итераций); арбитры решали в пользу extend из-за сходимости. При следующем iteration_limit оцени вниательно по контексту.
- **Визуальная проверка**: pending (финальная стадия).

## 6. Ключевые прецеденты арбитражей (для консистентности решений)

- `case_letter_conflict` (дубли кейсов/зон/префиксов Ts) — устойчиво решался **reissue_case** (a-129..a-153), каноническая зона закреплялась по фактическому дереву, дубли выводились из компиляции/прогонов.
- Оракулы `/swagger` (TS-002, TS-003): a-148/a-152 — **reissue_case** с амендой «конечная точка — Swagger UI с 200, редирект-цепочка допущена, тело не index.html, Production — TS-009».
- `iteration_limit` серии scenario_review: a-134/a-140/a-141/a-149/a-154/a-155 — **extend_iterations** (+2/+3) при сходимости; **redo_scenarios** применялся только при доказанном регрессе (a-135, a-150, a-145).
- Точка budget: после двух фейлов по бюджетам — max_arbitrations исчерпывался (поднят 130→200→больше), max_wall_clock_min исчерпывался (resume сбрасывает окно).

## 7. Полезное

- Статус в любой момент: `python3 -m pipeline status --run-dir <run>` (обновляет `<run>/dashboard.md`) или `/pipeline:status` из любой сессии.
- Ручное замещение шага (если что-то проверено вне конвейера): `python3 -m pipeline override --run-dir R close-batch|reissue-case|known-issue ...`.
- Не редактировать `state.json` / `journal.jsonl` руками — `verify` это детектирует.
