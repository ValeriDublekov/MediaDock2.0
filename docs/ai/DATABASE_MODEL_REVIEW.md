# Преглед на модела на базата данни

**Статус:** имплементиран и проверен върху disposable PostgreSQL; persistent DB reset не е изпълнен и изисква отделно потвърждение за всяка среда
**Дата:** 2026-09-30

## Цел и решения

Да се изчистят инвариантите на EF/PostgreSQL модела преди натрупване на данни. `titles` остава общият запис за OMDb метаданните, `occurrences` пази само реални torrent наблюдения, а Oscar данните остават в отделните таблици. Oscar-only филмите се показват в Oscar каталога, не в общия torrent каталог. Предпочита се целенасочена преработка и една нова базова миграция, а не универсален модел за произволни бъдещи каталози.

Основните проблеми са: липсва уникалност на `titles.imdb_id`; съвпадението по заглавие/година е евристика без защита при конфликтен IMDb ID; Oscar placeholder получава фиктивни `first_seen_at`/`last_seen_at`; общият каталог чете и заглавия без `occurrences`; `settings` допуска повече от един ред. Има и няколко липсващи ограничения за статуси и времена. Това са установени рискове в [конфигурациите](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/CatalogConfigurations.cs), [RSS upsert-а](../../server/src/MediaDock.Infrastructure/Ingestion/PostgresRssIngestionRepository.cs), [Oscar importer-а](../../server/src/MediaDock.Infrastructure/OscarAwards/OscarDatasetImporter.cs) и [каталожната заявка](../../server/src/MediaDock.Api/Catalog/CatalogApiService.cs).

## Стъпка 1: Идентичност и времеви полета

1. В [Title](../../server/src/MediaDock.Infrastructure/Persistence/Entities/Title.cs) направи `FirstSeenAt` и `LastSeenAt` nullable: те означават първо и последно **torrent наблюдение**, а не дата на импорт или OMDb обогатяване. `UpdatedAt` остава задължително. Наложи в базата и двете seen дати да са едновременно `NULL` или едновременно попълнени, а при попълване `first_seen_at <= last_seen_at`. Добави `first_seen_at <= last_seen_at` и за `occurrences`.
2. Нормализирай IMDb ID еднакво при вход от CSV и OMDb и при търсене. Добави уникален частичен индекс върху наличен, непразен `titles.imdb_id`. Запази търсенето по `(normalized_title, year, media_type)` като **неуникален** индекс: два различни филма могат да имат еднакво заглавие и година. В [TitleConfiguration](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/CatalogConfigurations.cs) замени индекса само по `normalized_title`, ако новият композитен индекс покрива използваните заявки.
3. При fallback по име/година свързвай съществуващ `Title` само ако IMDb ID липсва или съвпада с входния. При два различни ненулеви IMDb ID не сливай записите. В [OscarDatasetImporter](../../server/src/MediaDock.Infrastructure/OscarAwards/OscarDatasetImporter.cs) и [Oscar enrichment repository](../../server/src/MediaDock.Infrastructure/OscarAwards/PostgresOscarEnrichmentRepository.cs) провери съответствието между `OscarFilm.ImdbId`, `Title.ImdbId` и резултата от OMDb, преди да променяш FK или ID. При конкурентно създаване за един IMDb ID обработвай само съответното unique violation: рестартирай операцията с чист EF context/транзакция и преизчети каноничния запис; не маскирай други грешки.

**Критерии:** два записа с един IMDb ID се отхвърлят; едноименни различни филми остават допустими; конфликтен OMDb резултат не презаписва идентичността на друг филм; Oscar-only заглавие има `NULL` seen дати.

## Стъпка 2: Инварианти на останалите таблици

1. В [SettingsConfiguration](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/SettingsConfiguration.cs) наложи `CHECK (id = 1)`. В [SourceSettingsApiService](../../server/src/MediaDock.Api/Sources/SourceSettingsApiService.cs) и Worker четенията използвай ключ `1`, с атомарно създаване/обновяване или обработка на конкурентна първа инициализация. Не разчитай на `OrderBy(Id).FirstOrDefault()` като гаранция за singleton.
2. В [OscarConfigurations](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/OscarConfigurations.cs) ограничи `enrichment_status` до `pending`, `enriched`, `not_found`, `temporary_error` и `enrichment_attempt_count >= 0`.
3. В [OperationalConfigurations](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/OperationalConfigurations.cs) и [OscarEnrichmentRunConfiguration](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/OscarEnrichmentRunConfiguration.cs) наложи: `running` има `finished_at IS NULL`, всеки завършил статус има `finished_at IS NOT NULL`. Съществуващите writer-и задават крайния статус и време заедно; прекъснат процес може основателно да остави `running` запис.

**Критерии:** базата отхвърля втори settings ред, невалиден Oscar статус/отрицателни опити и несъвместими run статус и крайно време.

## Стъпка 3: Запис и четене на каталозите

1. `CreatePlaceholderTitle` в Oscar importer-а не попълва torrent seen дати. [TitleMetadataMapper](../../server/src/MediaDock.Infrastructure/Metadata/TitleMetadataMapper.cs) и RSS upsert-ът задават първо и последно наблюдение при първата действителна поява в feed; следващите появи обновяват последното. Oscar enrichment не ги променя. Запази съществуващите уникални `stable_key` и `import_key`, както и разделението между Oscar и RSS записи.
2. В [CatalogApiService](../../server/src/MediaDock.Api/Catalog/CatalogApiService.cs) ограничи общия **списък** до заглавия с поне един `Occurrence`, включително броя за пагинация. Oscar-only записи остават в Oscar API. Директният детайлен маршрут по `title_id` може да остане достъпен за навигация от Oscar каталога.
3. [CatalogContracts](../../server/src/MediaDock.Api/Catalog/CatalogContracts.cs) и [web API типовете](../../web/src/api/types.ts) трябва да допускат липсващи seen дати в детайла. Съществуващият [date formatter](../../web/src/shared/format.ts) вече показва `Not recorded` за `null`. Списъчният `LastSeenAt` може да остане задължителен само ако writer-ите и тестовете доказват, че всеки показан запис с occurrence го има; иначе направи и него nullable.

**Критерии:** след Oscar импорт филмът е само в Oscar списъка; след първо RSS наблюдение влиза в общия каталог с коректни дати, без нов дублиран `Title` при съвпадащ IMDb ID.

## Стъпка 4: Нова базова миграция и документация

1. Прегледай шестте стари [EF миграции](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/) за ръчни SQL операции, ограничения и начални стойности. След стабилизиране на модела генерирай една нова начална миграция и snapshot; премахни стария помощен model builder, ако вече не е нужен. Не редактирай ръчно генерираните designer файлове.
2. Описанието за пресъздаване на БД трябва да е **изрична операция за всяка среда**: провери липсата на нужни данни, направи защитен backup, спри Writer-ите, пресъздай схемата и приложи новата миграция върху празна база, после провери API/Worker. [Compose](../../compose.yaml) запазва `postgres_data`, а [deploy скриптът](../../deploy/deploy.sh) стартира миграциите; новата начална миграция не може да се приложи върху стара `__EFMigrationsHistory` без reset. Не добавяй автоматично изтриване на volume в deploy.
3. При изпълнение синхронизирай [DATA_CONTRACTS](DATA_CONTRACTS.md), [Oscar плана](OSCAR_CATALOG_PLAN.md) и [операционния runbook](../../deploy/systemd/README.md) с новите инварианти. `ImportedAt` остава време на първия импорт, `UpdatedAt` на последното записване; не добавяй ново поле само за преименуване на тази семантика.

## Стъпка 5: Проверки преди приемане

- Разшири [PersistenceTests](../../server/tests/MediaDock.IntegrationTests/PersistenceTests.cs): чиста база с една миграция, уникалност на IMDb ID, допустими едноименни филми, seen range, settings singleton, валидни статуси и run времена.
- Разшири [OscarDatasetImporterTests](../../server/tests/MediaDock.IntegrationTests/OscarDatasetImporterTests.cs) за повторен импорт без дублиране и без фиктивни дати; [OscarEnrichmentRepositoryTests](../../server/tests/MediaDock.IntegrationTests/OscarEnrichmentRepositoryTests.cs) за конфликтен IMDb ID; [IngestionTests](../../server/tests/MediaDock.IntegrationTests/IngestionTests.cs) за Oscar-only -> RSS и конкурентно създаване; [CatalogApiTests](../../server/tests/MediaDock.IntegrationTests/CatalogApiTests.cs) и [OscarApiTests](../../server/tests/MediaDock.IntegrationTests/OscarApiTests.cs) за разделянето на списъците и детайлния маршрут.
- Стартирай `dotnet test` за unit и integration проектите (последните изискват Docker/Testcontainers), `npm run build` в `web`, проверка за pending EF model changes и миграция върху нова PostgreSQL база. Не приемай само успешна компилация за доказателство на identity и timestamp правилата.

## Допълнителни подобрения след основния етап

- Общото търсене използва `ILIKE '%...%'`; обикновен B-tree индекс не ускорява този шаблон. При реалистичен обем измери с `EXPLAIN (ANALYZE, BUFFERS)` и тогава обмисли trigram/GIN. За Oscar кандидатите измери заедно филтъра по status/retry и сортирането по `film_year`, преди да добавиш индекс. Не добавяй индекс на `oscar_films.imdb_id` без заявка по него; `omdb_daily_usage.utc_date` вече е първичен ключ.
- Определи срок за пазене/архивиране на растящите `parse_logs` и `scan_runs`. Пълните PostgreSQL backup-и включват plaintext OMDb ключа от `settings` и трябва да останат защитени.
- Отделни задачи: атомарен upsert за metadata cache при конкурентни заявки и откриване на изоставени `running` записи. Не добавяй heartbeat поле без работещ процес, който го обновява; не налагай `last_enrichment_error IS NOT NULL` за `temporary_error`, докато всеки временен отказ не гарантира код.

Извън обхвата са универсална схема за множество award източници, speculative `metadata_authority`/`origin` полета и автоматично сливане по име/година. Реалните данни липсват, но зануляване на persistent база все пак изисква отделно потвърждение за съответната среда.