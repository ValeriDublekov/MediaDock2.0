# План: Golden Globes enrichment и ръчно IMDb свързване

**Статус:** Phase B реализирана; останалите етапи са планирани
**Последна редакция:** 2026-10-07

## Цел

Да се подобри автоматичното намиране на Golden Globes заглавия в OMDb и да може операторът да зададе или коригира IMDb ID за всяко заглавие, независимо дали то е `pending`, `enriched`, `problem`, `not_found` или `temporary_error`. Ръчното задаване трябва да стартира надеждно обновяване на метаданните по IMDb ID, а не ново търсене по заглавие.

## Текущо поведение и установени пропуски

- Импортът пази номинациите отделно от каноничните `titles`; типът на номинацията се нормализира до `movie` или `series`.
- Enrichment, API и UI групират по `(церемониална година, заглавие, тип)` и API връща стабилен `filmId` за тази група.
- Автоматичният lookup използва OMDb `t=` без година. `and` и `&` не се нормализират като еквивалентни варианти. Ако OMDb върне филм от несъвпадаща година, проверката става след lookup и резултатът се маркира `problem`.
- Catalog metadata се чете с реалния nominee type; ръчните връзки четат cache по точен IMDb ID и тип.
- Текущият service задава `enriched` само при валиден IMDb ID, но няма invariant/test за вече записан или импортнат ред в състояние `enriched` без ID.
- Детайлният каталог позволява задаване, смяна и изчистване на manual IMDb ID; задаването атомарно създава target-specific refresh job, а link version обезсилва остарели резултати.

## Предложен обхват

### 1. Единен ключ на заглавие

- Уеднакви групирането в repository, API и UI по `(ceremonyYear, title, nomineeType)`.
- API да връща стабилен `filmId` за тази група и достатъчно информация за типа. Не използвай само годината и текста на заглавието, ако това може да слее movie и series записи.
- Lookup и update да засягат всички nomination редове в групата в една транзакция; други церемониални години със същото заглавие не се променят.

### 2. Ръчно IMDb ID задаване и metadata refresh

- Добави детайлен UI контрол за задаване, смяна и изчистване на IMDb ID. Контролът е достъпен при всеки enrichment статус; при смяна на вече намерен ID потребителят вижда, че ще се заменят свързаните metadata.
- Добави валидиран API mutation за групата. Нормализирай ID към lowercase `tt` формат и проверявай формата преди запис; OMDb lookup потвърждава, че ID действително съществува.
- Запиши ръчното свързване и durable refresh request атомарно, за да не остане зададен ID без задача за обновяване. Задачата използва точния IMDb ID (`i=`), заявява metadata за правилния `movie`/`series` тип, записва резултата в съществуващия metadata cache и обновява статуса на цялата група.
- Refresh за ръчно зададен ID заобикаля стария negative title cache и автоматичното title matching. Успешно обогатяване изисква OMDb да върне същия нормализиран ID и съвместим тип.
- Ръчното свързване има предимство пред автоматичните резултати: batch matcher не може тихо да го замени. Пази източника на връзката (например `automatic`/`manual`) или еквивалентен pin флаг в persistence.
- За повторни промени, докато задача за стария ID още работи, не допускай старият резултат да презапише новия избор. Проверявай текущия ID/version преди записване или сериализирай и обезсилвай остарелите заявки.
- Използвай съществуващия API-hosted dispatcher/advisory lock и общия OMDb budget. Ръчната операция е durable и проследима като job; API отговаря с job идентификатор, а UI показва queued/running/error/success чрез съществуващия job статус.
- Не записвай API ключове, provider URL-и или сурови provider грешки в payload/events. При временен проблем или изчерпан budget пази ID избора и показва състояние за retry; при невалиден/ненамерен ID обяснява проблема, без да показва несвързани стари metadata.
- Изчистването на ръчно зададен ID маха pin-а и позволява следващо автоматично търсене. Повторен CSV/TSV import не изтрива ръчната връзка; корекция на nominee type трябва да я провери отново и да не я запазва безусловно.

### 3. Metadata cache и статуси

- Търси metadata в cache с реалния `nomineeType`, а не с фиксиран `movie`.
- Каталогът трябва да показва IMDb ID независимо от наличието на рейтинг или постер. Липсващо поле от OMDb не означава неуспешно свързване.
- Поддържай invariant: `enriched` означава потвърден IMDb ID и успешно получени metadata за същия ID и съвместим тип. Добави проверка при запис/проекция, така че `enriched` без ID да не се представя като здрав резултат.
- При внедряване отчети съществуващите записи `enriched` без IMDb ID и ги върни в състояние за повторна обработка; прегледай тези редове преди миграция/масова корекция.

### 4. Автоматично търсене с OMDb кандидати

- Запази евтиния точен `t=` lookup като първи опит. При `not_found` или вероятно грешен резултат (например филм извън допустимия период) използвай OMDb Search `s=` като fallback.
- Търси безопасни варианти на заглавието, включително `and`/`&` и разлики в пунктуацията/whitespace; не променяй оригиналното заглавие в импортните данни.
- Филтрирай и оцени резултатите по нормализирано заглавие, nominee type и година. За филми допустимият автоматичен период остава церемониалната година или предходната; при сериали церемониалната година не е година на премиера и не е филтър.
- Приемай автоматично само еднозначен кандидат над зададен праг на увереност. При двусмислени или слаби съвпадения запазвай `problem` с безопасен код, вместо да свързваш грешен IMDb запис.
- След избора извлечи пълните metadata по кандидатския IMDb ID и потвърди ID/тип преди запис. Search list не е заместител на детайлния lookup.
- Страници и повторни search заявки, detail lookup и retry се отчитат като отделни реални HTTP attempts в споделения budget. Кешираните резултати не консумират HTTP budget.

## Данни и API промени

- Добави ръчна/автоматична връзка за IMDb ID към persistence; миграцията трябва да запази съществуващите ID, статуси, retry полета и идемпотентността на импорта.
- Добави mutation endpoint за IMDb ID и refresh job; дефинирай idempotency за повтарящи се заявки към същата група и ID.
- Обнови Golden Globes response contract и групирането, така че UI да изпраща точния `filmId` и да показва type-aware metadata.
- Ръчните endpoints остават в същата доверена локална/LAN граница като останалите конфигурационни операции; тази промяна не добавя authentication.

## Етапи

- [ ] **A. Групова идентичност и cache fix:** уеднаквяване на ключа `(година, заглавие, тип)`, поправка за сериалния cache lookup и тестове за IMDb ID срещу липсващ рейтинг/poster.
- [x] **B. Ръчно свързване:** persistence за manual pin, валидиран API mutation, durable target-specific refresh, UI за задаване/смяна/изчистване и защита от остарял job резултат.
- [ ] **C. OMDb Search fallback:** multi-result DTO/client, кандидатско оценяване, type/year проверки, нееднозначен резултат без автоматично свързване и budget accounting.
- [ ] **D. Съществуващи данни и acceptance:** отчет за `enriched` без ID, безопасна повторна обработка, regression tests, миграция и операторска проверка с `Abbott Elementary`, `and`/`&`, омоними/години и movie/series.

## Тестове и критерии за приемане

- Импортът и API не сливат еднакво изписано заглавие от различни типове; груповите промени засягат само точната година/заглавие/тип.
- Въвеждане на валиден IMDb ID за заглавие във всеки от петте статуса създава durable refresh и OMDb заявка по ID; след успех ID, рейтинг и други налични полета се показват.
- Невалиден ID, provider not-found, несъвместим type, quota stop и временна грешка не изтриват ръчния избор и се показват като безопасни състояния.
- Смяната/изчистването на ID работи; повторен импорт не маха manual pin; стар job за предишния ID не може да презапише последния избор.
- `Abbott Elementary` търси и чете metadata като `series`; пропуснат рейтинг от OMDb не скрива наличния IMDb ID.
- Title normalization намира варианти `and`/`&`; автоматичното обогатяване избира правилна година, а при двусмислие не избира произволен кандидат.
- Всички реални OMDb заявки са отчетени в общия дневен budget; тестовете за API/UI, repository, enrichment и dispatcher минават през `python -B scripts/run_tests.py <suite>` според [testing guide](TESTING.md). Интеграционните тестове използват наличния Docker setup.

## Handoff: Phase B (2026-10-07)

- Реализирани са manual pin, exact-ID OMDb refresh, atomic job enqueue, ID-keyed metadata cache projection, UI status polling и version guard за остарели резултати.
- Повторна заявка за същите група/ID използва активната задача. Смяна и изчистване увеличават версията; type correction премахва manual pin.
- Проверки: `python -B scripts/run_tests.py server-unit --filter "Category=GoldenGlobes"`, `python -B scripts/run_tests.py server-integration --filter "FullyQualifiedName~GoldenGlobe"` и `python -B scripts/run_tests.py web`.
- Следваща отправна точка: Phase C, OMDb Search fallback и безопасно оценяване на кандидати.

## Проектни отправни точки

- [Golden Globes enrichment service](../../server/src/MediaDock.Application/GoldenGlobes/GoldenGlobeEnrichmentService.cs)
- [Golden Globes API service](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeApiService.cs)
- [OMDb client](../../server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs)
- [Metadata resolver](../../server/src/MediaDock.Application/Metadata/MetadataResolver.cs)
- [Golden Globes data contracts](DATA_CONTRACTS.md)
- [API contracts](API_CONTRACTS.md)
- [Architecture](ARCHITECTURE.md)
- [OMDb API](https://www.omdbapi.com/)