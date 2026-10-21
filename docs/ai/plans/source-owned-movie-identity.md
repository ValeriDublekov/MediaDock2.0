# План: Source-Owned Movie Identity

## Цел и договорени решения

Премахваме `titles` като обща връзка между Oscar, Golden Globes и RSS каталога. Всеки източник притежава собствените си основни записи, а намерената IMDb идентичност се споделя чрез `imdb_titles`.

- Три основни source-owned таблици: `oscar_movies`, `golden_globe_titles` и `torrent_movies`.
- Всяка таблица може да сочи към `imdb_titles.imdb_id`; липсата на IMDb съвпадение не трябва да пречи на импорта.
- `imdb_titles` е единственият собственик на provider metadata. Source-owned таблиците пазят source identity и IMDb връзка, без копия или fallback metadata; несъвпадналите записи остават без provider metadata.
- Golden Globes запазва както филмите, така и сериалите чрез `nominee_type`.
- Номинациите и отделните torrent/RSS издания остават в detail таблици. `torrent_movies` е по един запис на филм, към който могат да сочат множество torrent издания.
- `imdb_titles` остава каноничният собственик на IMDb metadata. За добавяне във Favorites metadata трябва да е успешно заредена (`metadata_status = 'enriched'`), не само да има IMDb ID.
- Favorites се ключват по `(user_id, imdb_id)`, така че един филм от различни източници е един favorite за даден потребител.
- `ToDownload` е marker за бъдещо намиране и не изисква вече наличен RSS torrent.
- Съществуващи favorites без успешно обогатен IMDb запис се изтриват при миграцията. Това е избрано поведение, което води до необратима загуба при обикновен rollback.

## Фази

### 1. Модел и миграция

1. Определи полетата и ограниченията на трите основни таблици. Запази Oscar film identity и enrichment, Golden Globes година/тип/IMDb linking, както и RSS catalog identity. Provider metadata се чете само от `imdb_titles`; не копирай legacy fallback полетата от `titles` в `torrent_movies`.
2. Запази Oscar nominations, Golden Globes awards/nominations и torrent releases като свързани detail записи. Всеки RSS item трябва да запази source, item key, URL, raw title, quality, rip type и времевите полета.
3. Определи idempotency и merge правилата за `torrent_movies`: IMDb ID да обединява известните съвпадения; title/year/type не е достатъчно доказателство за безопасно сливане, когато IMDb липсва или има конфликт. Запази отделните releases по source item identity.
4. Добави EF Core миграция, която прехвърля awards, nominations, RSS sources/releases и свързаните данни преди премахването на `titles` и старите връзки.
5. Мигрирай `favorite_movies` към `(user_id, imdb_id)` само за свързани IMDb записи със `metadata_status = 'enriched'`. Изтрий непреносимите редове; запази `ToWatch`, `ToDownload` и owner scoping на пренесените favorites. Добави preflight/тест за броя на редовете, които ще бъдат изтрити.
6. Премахни `titles` едва след като новите FK-и и всички четци/писатели са пренасочени. При премахването legacy provider fallback стойностите в `titles` умишлено се изхвърлят, а не се прехвърлят в `torrent_movies`; преброй ги в preflight и тествай този резултат. Подготви backup преди rollout; не приемай, че `Down` може да възстанови изтритите favorites или fallback metadata.

### 2. Импорт, enrichment и RSS

1. Пренасочи Oscar import/enrichment към `oscar_movies`, без задължителен `TitleId`; запази imported source ID, enrichment state и всички nomination факти.
2. Пренасочи Golden Globes import/enrichment към `golden_globe_titles`; запази групирането по title/year/nominee type, manual linking, сериалите и всички awards/nominations.
3. Пренасочи RSS ingestion и catalog read paths към `torrent_movies` и release таблицата. Запази уникалността по source/item key, идемпотентността и torrent metadata.

### 3. Favorites API и web

1. Смени API заявките и отговорите от `titleId` към `imdbId`; `POST` допуска само съществуващ IMDb запис със `metadata_status = 'enriched'`. Самият FK не е достатъчен, защото `imdb_titles` съдържа и `pending` записи.
2. Смени favorite lookup/update/delete ключовете към `(current_user_id, imdb_id)`. Запази съществуващата изолация между потребителите.
3. Премахни проверката, която блокира `ToDownload` при липса на torrent occurrence.
4. Обнови React API типове, `FavoriteContext`, controls и views да използват IMDb ID. Покажи споделеното състояние на поддържаните Oscar, Golden Globes movie и torrent/catalog екрани; не позволявай добавяне от pending или unmatched запис.

### 4. Тестове и документация

1. Обнови миграционните, persistence, import, API и web тестове за новите таблици и договори.
2. Актуализирай data schema/contracts/behavior и catalog/awards API документацията.
3. Провери локалните Markdown линкове.

## Критерии за приемане

- Една и съща enriched IMDb идентичност от различни източници се показва като един favorite на потребител.
- IMDb ID със статус `pending`, `not_found` или `temporary_error`, както и запис без IMDb ID, не може да се добави във Favorites.
- Provider metadata се предоставя единствено от `imdb_titles`; `torrent_movies` и останалите source-owned записи не съдържат fallback metadata, включително при липсващо IMDb съвпадение.
- Enriched филм може да бъде маркиран `ToDownload`, преди да има RSS torrent.
- Множество RSS издания остават отделни и са свързани с един `torrent_movies` запис, когато идентичността е известна.
- Oscar номинациите и Golden Globes awards/nominations, включително сериалите, се запазват след миграцията.
- Непреносимите стари favorites се изтриват; преносимите запазват user ownership и marker стойностите.
- Импортите на Oscar и Golden Globes, както и повторното RSS сканиране, остават идемпотентни. RSS изданията са уникални по `(source_id, source_item_key)`; отделни издания споделят `torrent_movies` запис само при съвпадащ известен IMDb ID, а липсващи или противоречащи IMDb ID-та не се обединяват само по title/year/type.

## Основни файлове

- Persistence: source-owned entity/configuration files, `MediaDockDbContext.cs`, `SourceOwnedMovieIdentity`, and `RemoveLegacyTitleIdentity`; historical legacy rows are represented only in the test-only `LegacyPersistenceTestContext`.
- Imports и ingestion: `server/src/MediaDock.Infrastructure/OscarAwards/OscarDatasetImporter.cs`, `server/src/MediaDock.Infrastructure/GoldenGlobes/GoldenGlobeDatasetImporter.cs`, `server/src/MediaDock.Infrastructure/Ingestion/PostgresRssIngestionRepository.cs`, `server/src/MediaDock.Application/Ingestion/RssEntryProcessor.cs`.
- API: `server/src/MediaDock.Api/Favorites/FavoriteApiService.cs`, `FavoriteContracts.cs`, `FavoriteEndpoints.cs`, както и catalog/Oscar/Golden Globes services и contracts, които използват `titleId`.
- Web: `web/src/api/client.ts`, `web/src/api/types.ts`, `web/src/features/favorites/FavoriteContext.tsx`, `FavoriteControls.tsx`, `FavoritesView.tsx`, и съответните catalog/Oscar/Golden Globes views.
- Tests: `server/tests/MediaDock.IntegrationTests/PersistenceTests.cs`, `IngestionTests.cs`, `OscarDatasetImporterTests.cs`, `GoldenGlobeDatasetImporterTests.cs`, `GoogleAuthenticationApiTests.cs`, плюс засегнатите web tests под `web/src/features/`.
- Документация: `docs/ai/DATA_SCHEMA.md`, `DATA_CONTRACTS.md`, `DATA_BEHAVIOR.md`, `API_CATALOGS_AND_AWARDS.md`, `API_CONTRACTS.md`.

## Проверки

Използвай repository runner за всеки test run:

```powershell
python -B scripts/run_tests.py server-integration --filter "Category=Persistence"
python -B scripts/run_tests.py server-integration --filter "Category=Ingestion"
python -B scripts/run_tests.py server-unit --filter "Category=GoldenGlobes"
python -B scripts/run_tests.py server-integration --filter "Category=Api"
python -B scripts/run_tests.py web --filter favorites
python -B scripts/check_markdown_links.py
```

Integration тестовете изискват Docker; ако Docker не е наличен, докладвай това като blocker. За web suite добави тесни филтри за променените catalog/Oscar/Golden Globes компоненти. Не изпълнявай live RSS или OMDb заявки.

## Извън обхвата

- Не се добавя реален download executor; променя се само `ToDownload` marker-ът.
- Не се променят външните metadata доставчици или fuzzy matching правилата.
- Не се премахва `imdb_titles`; тя остава общата IMDb identity и metadata таблица.
