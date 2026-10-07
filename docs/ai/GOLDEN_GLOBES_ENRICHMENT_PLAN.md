# План: Golden Globes enrichment и ръчно IMDb свързване

**Статус:** Phase A, B и C реализирани; Phase D acceptance е в ход
**Последна редакция:** 2026-10-07

## Цел

Да се подобри автоматичното намиране на Golden Globes заглавия в OMDb и да може операторът да зададе или коригира IMDb ID за всяко заглавие, независимо дали то е `pending`, `enriched`, `problem`, `not_found` или `temporary_error`. Ръчното задаване трябва да стартира надеждно обновяване на метаданните по IMDb ID, а не ново търсене по заглавие.

## Текущо поведение и установени пропуски

- Импортът пази номинациите отделно от каноничните `titles`; типът на номинацията се нормализира до `movie` или `series`.
- Enrichment, API и UI групират по `(церемониална година, заглавие, тип)` и API връща стабилен `filmId` за тази група.
- Автоматичният lookup започва с OMDb `t=` без година и използва Search `s=` fallback при not-found или неправдоподобен резултат. Кандидатите се оценяват по нормализирано заглавие, тип и допустима година; близките кандидати се проверяват с подробен lookup по IMDb ID и OMDb `Awards` трябва да споменава Golden Globe, за да се приеме еднозначно съвпадение.
- Catalog metadata се чете с реалния nominee type; ръчните връзки четат cache по точен IMDb ID и тип.
- Автоматичният и ръчният enrichment потвърждават IMDb ID и nominee type преди `enriched`; повредени legacy редове се проектират като `pending` и се включват за повторна обработка.
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

- [x] Търси metadata в cache с реалния `nomineeType`, а не с фиксиран `movie`.
- [x] Каталогът показва IMDb ID независимо от наличието на рейтинг или постер. Липсващо поле от OMDb не означава неуспешно свързване.
- [x] Поддържай invariant: `enriched` означава потвърден валиден IMDb ID и успешно получени metadata за същия ID и съвместим тип. Проверявай автоматичните/ръчните резултати и записите; повредените записи не се проектират като здрав резултат.
- [x] Enriched редове без валиден IMDb ID се включват за повторна обработка. Преди внедряване или ръчна масова корекция прегледай само-четящия отчет:

```sql
SELECT year, nominee_type, title, import_key, imdb_id, last_enrichment_error
FROM golden_globe_nominations
WHERE enrichment_status = 'enriched'
	AND (imdb_id IS NULL OR imdb_id !~ '^tt[0-9]{7,10}$')
ORDER BY year DESC, title, nominee_type, import_key;
```

Не се изпълнява автоматична миграция или масова промяна на тези редове. Каталогът ги показва като `pending`, а enrichment worker ги избира за повторна обработка; операторът преглежда отчета преди отделна корекция на данните.

### 4. Автоматично търсене с OMDb кандидати

- Запази евтиния точен `t=` lookup като първи опит. При `not_found` или вероятно грешен резултат (например филм извън допустимия период) използвай OMDb Search `s=` като fallback.
- Търси безопасни варианти на заглавието, включително `and`/`&` и разлики в пунктуацията/whitespace; не променяй оригиналното заглавие в импортните данни.
- Филтрирай и оцени резултатите по нормализирано заглавие, nominee type и година. За филми допустимият автоматичен период остава церемониалната година или предходната; при сериали церемониалната година не е година на премиера и не е филтър.
- Използвай Search score за ранжиране, но не отхвърляй кандидата само заради score преди detail lookup. Извлечи детайлите по IMDb ID за най-близкия кандидат или близките кандидати. Приемай само един кандидат, чиито детайли потвърждават ID, тип, близко заглавие, допустима година и Golden Globe в OMDb `Awards`; при липсващо или двусмислено доказателство запазвай `problem` с безопасен код.
- Ако Search резултатите надхвърлят лимита на прочетените страници, запазвай `problem`, защото списъкът с кандидати е непълен.
- Search list не е заместител на детайлния lookup. OMDb `Awards` е свободен обобщен текст, а не структуриран запис за конкретна церемония или категория.
- Страници и повторни search заявки, detail lookup и retry се отчитат като отделни реални HTTP attempts в споделения budget. Кешираните резултати не консумират HTTP budget.

## Данни и API промени

- Добави ръчна/автоматична връзка за IMDb ID към persistence; миграцията трябва да запази съществуващите ID, статуси, retry полета и идемпотентността на импорта.
- Добави mutation endpoint за IMDb ID и refresh job; дефинирай idempotency за повтарящи се заявки към същата група и ID.
- Обнови Golden Globes response contract и групирането, така че UI да изпраща точния `filmId` и да показва type-aware metadata.
- Ръчните endpoints остават в същата доверена локална/LAN граница като останалите конфигурационни операции; тази промяна не добавя authentication.

## Етапи

- [x] **A. Групова идентичност и cache fix:** уеднаквяване на ключа `(година, заглавие, тип)`, поправка за сериалния cache lookup и тестове за IMDb ID срещу липсващ рейтинг/poster.
- [x] **B. Ръчно свързване:** persistence за manual pin, валидиран API mutation, durable target-specific refresh, UI за задаване/смяна/изчистване и защита от остарял job резултат.
- [x] **C. OMDb Search fallback:** multi-result DTO/client, кандидатско оценяване, type/year проверки, нееднозначен резултат без автоматично свързване и budget accounting.
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
- Metadata/status hardening: cache projection използва `nomineeType`; невалиден `enriched` ред се показва като `pending`, влиза за повторна обработка и е включен в read-only audit SQL по-горе. Enriched write без валиден IMDb ID се отхвърля.
- Phase C is now implemented; see the handoff below. Phase D remains.

## Handoff: Phase C (2026-10-07)

- `t=` остава първи опит; not-found и неправдоподобни точни резултати преминават към пагиниран `s=` fallback с варианти на пунктуацията и `and`/`&`.
- Search fallback зарежда детайлите по IMDb ID за най-близките кандидати; единствено еднозначен кандидат с потвърдени ID/тип/заглавие/година и Golden Globe в `Awards` се записва като `enriched`. Липсата на потвърждение записва безопасен `problem` код.
- Всеки search page и IMDb-ID detail lookup резервира отделен реален OMDb опит от споделения budget; cache hit-овете остават без HTTP опит.
- Проверки: `python -B scripts/run_tests.py server-unit --filter "Category=GoldenGlobes|FullyQualifiedName~OmdbClientBudgetTests"`; Phase D acceptance и операторските проверки остават.
- Следваща отправна точка: Phase D, regression/acceptance с production dataset, омоними и операторски преглед.

## Handoff: Повторен Search fallback за terminal статуси (2026-10-07)

- Ръчното Golden Globes enrichment вече избира и немануални групи със статус `not_found` или `problem`; ръчните IMDb pin-ове остават изключени. Следващо стартиране опитва тези групи отново.
- Search fallback взима детайли по IMDb ID за най-високо ранжираните близки кандидати и използва OMDb `Awards` като Golden Globe сигнал. Само еднозначен кандидат минава към `enriched`; без сигнал или при повече от един потвърден кандидат се записва `problem`.
- Проверки: `python -B scripts/run_tests.py server-unit --filter "Category=GoldenGlobes"` и repository eligibility интеграционният тест. OMDb Awards е свободен provider текст, затова остава операторска проверка при Phase D acceptance.

## Handoff: Production run не намери съвпадения (2026-10-07)

- Live API изпълни job `53` с commit `b94367f4cb299537a9ac7f3d181f7584689c50ef` и завърши `213/213` опита: `0 enriched`, `140 not_found`, `73 problem`, `489` HTTP опита, без временни грешки или quota stop.
- Read-only status audit: `59` групи са `problem/no_confident_match`, `14` са `problem/no_golden_globe_match`, `140` са `not_found`. `no_confident_match` обединява липса на ранжируем кандидат, непълни Search страници и предишния score cutoff; отчетът не разделя тези подпричини. `not_found` означава, че Search не е върнал usable кандидат.
- Локалната корекция премахва предварителния твърд score cutoff, така че най-близките кандидати да стигат до подробната проверка; строгите detail проверки и непълното Search guard остават. Unit suite минава `15/15`.
- Тази последна корекция още не е в commit или production. На `2026-10-07` общият OMDb budget е `502/1000` с `498` оставащи заявки; изчакай новия код да мине deployment gate и планирай повторния job след reset на UTC дневния budget, за да не изчерпи наличния остатък.

## Handoff: Enrichment върна 0 обработени (2026-10-07)

- Потребителят разполага с една среда и иска да тества вече импортираните номинации, без повторен импорт. Потребителят е разрешил SSH достъп и необходима целева промяна на базата, ако се окаже нужна.
- Последен резултат от потребителя: Golden Globes enrichment е завършил с 0 обработени записа. Причината още не е установена.
- Локалният API на `http://127.0.0.1:8080` отговори на readiness проверката. Локалният `docker compose ps` не можа да се изпълни, защото в тази workspace среда липсва `POSTGRES_PASSWORD`; това не доказва, че API или базата, използвани от потребителя, са спрени.
- SSH профилът `.server-access.sshconfig` съществува. Опитът за свързване и последващата заявка за агрегирани статуси бяха прекъснати от потребителя. Не са прочетени статуси от базата и не са правени промени по редове, job-ове или импорти.
- Batch worker избира само `pending`, `temporary_error` с настъпил `next_enrichment_attempt_at`, и `enriched` с невалиден/липсващ IMDb ID; manual-linked редове се пропускат. Здравите `enriched`, `problem` и `not_found` редове са крайни и не се обработват отново.
- Следваща сесия: свържи се с `ssh -F .server-access.sshconfig mediadock` и използвай съществуващата SSH сесия. Потвърди реалния deployment root и версията на приложението; `README.md` пази по-стар запис, че production release не е имал background-job миграции, така че не приемай този запис за текущ без проверка.
- Първо направи read-only проверка на последния Golden Globes job/events и агрегирани nomination статуси, включително `is_imdb_id_manual` и due retry времето. Не извеждай `.env`, пароли, API ключове или provider URL-и.
- Ако няма eligible групи, подготви конкретен списък по `(year, title, nominee_type)` и предварително потвърди броя и текущите стойности на редовете. Само тогава, с разрешението на потребителя, постави в `pending` минималните подходящи немануални групи и нулирай техните retry/error полета. Не променяй IMDb ID, manual pin, други години или несвързани заглавия; не преимпортирай файловете.
- Ако eligible редове има, не променяй базата: установи защо job-ът не ги е обработил чрез job events, dispatcher логове и версията на приложението. Ако средата работи със стара версия, използвай само документирания deployment/backup/migration gate преди нов опит.
- След корекцията или потвърдена конфигурация стартирай съществуващия enrichment job, после провери processed summary, статуса на целевите групи и OMDb budget usage. Потребителят не очаква нов импорт.

## Handoff: Нулевите eligible групи са обработени (2026-10-07)

- Потвърдено е, че live API работи с текущия source commit и базата е достъпна; няма активен job.
- Последният предходен enrichment job завърши успешно с `EligibleTitles=0`. Причината е очакваната eligibility логика: всичките 1,456 групи са били крайни (`enriched` 1,241, `not_found` 166, `problem` 49); няма `pending`, due retry или повреден `enriched` ID. Няма manual-pinned групи.
- За минимален acceptance тест е върната само групата `(2024, "Daisy Jones and the Six", series)` в `pending`. Беше потвърден един ред, предишно `not_found`, без IMDb ID и без manual pin. Изчистени са само статусът, retry времето и последната грешка; IMDb ID, pin и attempt count (`1`) са запазени.
- Новият enrichment job завърши успешно: `EligibleTitles=1`, `AttemptedTitles=1`, `EnrichedTitles=1`, `HttpAttempts=4`, `CacheHits=0`, без quota stop или грешки. Групата вече е `enriched` с валиден IMDb ID `tt8749198`; import не е повтарян и други редове не са променяни.
- Дневното OMDb броячно използване се увеличи от 7 на 11 при лимит 1,000; няма quota/error флаг.
- Това потвърждава единичния acceptance сценарий за `and`/`&`. Останалите Phase D проверки за омоними/години и съпоставяне movie/series остават отделно.

## Handoff: IMDb metadata липсват при enriched запис (2026-10-07)

- Read-only одитът на live каталога показа само една засегната група: `(2024, "Daisy Jones and the Six", series)`, IMDb `tt8749198`. API връща `enriched` и ID, но празни `imdbRating`/`posterUrl`.
- Metadata cache съдържа валиден `found` payload за същия ID и тип с рейтинг `8.1`, 47,051 гласа, жанрове, сюжет и постер. Едновременно има `confirmed_not_found` title cache ред със същия `fetched_at`; автоматичната catalog проекция избира само най-новия title-cache ред и tie-ът може да избере отрицателния резултат.
- Проверени са всичките 1,242 enriched групи: 1,241 имат съвпадаща проекция; Daisy е единствената открита cache ambiguity. Номинационният ред не е променян и не е нужно ново enrichment/OMDb обаждане за него.
- Локалната корекция е catalog fallback към cache lookup по точен IMDb ID и nominee type, когато title-cache projection не даде валидни съвпадащи metadata. Regression тестът моделира по-нов отрицателен title cache и валиден exact-ID payload.
- Проверки: `python -B scripts/run_tests.py server-integration --filter "FullyQualifiedName~GoldenGlobeApiTests"` (2/2 passed). Промяната още не е внедрена в production; Daisy ще се визуализира коректно след нормалния deployment gate.

## Проектни отправни точки

- [Golden Globes enrichment service](../../server/src/MediaDock.Application/GoldenGlobes/GoldenGlobeEnrichmentService.cs)
- [Golden Globes API service](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeApiService.cs)
- [OMDb client](../../server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs)
- [Metadata resolver](../../server/src/MediaDock.Application/Metadata/MetadataResolver.cs)
- [Golden Globes data contracts](DATA_CONTRACTS.md)
- [API contracts](API_CONTRACTS.md)
- [Architecture](ARCHITECTURE.md)
- [OMDb API](https://www.omdbapi.com/)