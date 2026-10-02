# План: Лични IMDb оценки в MediaDock 2.0

**Статус:** импортът е реализиран; Oscar филтърът остава планиран

**Дата:** 2026-10-02

## Цел и решения

Да се пазят личните оценки на собственика на инсталацията и в Oscar каталога да има филтър „Скрий оценените от мен“. Проектът остава с един профил и без система за вход. Качването е в Configuration и е адитивно: повторен импорт добавя нови IMDb ID и обновява променени стойности; записите, които липсват от отделен файл, никога не се изтриват. Общият `Title.ImdbRating` от OMDb не е лична оценка и не се променя.

Автоматичното четене на [публичната страница с оценки](https://www.imdb.com/user/p.yevakmhgrhquq5pqfidzer4w74/ratings/) не е част от първата версия: [условията на IMDb](https://www.imdb.com/conditions) ограничават robots/screen scraping без изрично разрешение. [Официалният IMDb API](https://help.imdb.com/article/imdb/general-information/introducing-the-imdb-api/G49M5Y59L5N4WABM) се предлага през AWS Data Exchange с пробен достъп; не е потвърден безплатен endpoint за оценки на конкретен потребител. Ръчен бутон и ежедневна синхронизация от URL могат да се обсъдят само след установяване на разрешен източник и условията му.

## Предусловие: получаване на данните

За акаунт, мигриран към Amazon, [IMDb Request my data](https://www.imdb.com/registration/data-requests/?ref_=acset) показва **Manage your Amazon account**, а не **Submit request**. Оттам собственикът влиза със същия Amazon акаунт и проверява [Amazon Request My Data](https://www.amazon.com/hz/privacy-central/data-requests/preview.html) за категория IMDb или друга опция, която включва IMDb данните. Тази страница изисква вход: наличните категории и съдържанието на архива не са потвърдени. Указанията на IMDb за потвърждение до 5 дни и изтегляне до 90 дни се отнасят до директната IMDb заявка, не са потвърдени за заявката през Amazon.

Ако там няма оценки, проверете [Your Ratings](https://www.imdb.com/list/ratings/) за отделна опция за export; ако липсва, [свържете се с IMDb](https://help.imdb.com/contact?deepLink=privacy&disableLoginPopup=true) с изрична молба за личните оценки с IMDb title ID и стойност на оценката. Не се предполага, че текущият интерфейс предлага CSV или JSON.

Потвърденият JSON формат е масив от обекти `{ "id": "tt14452776", "rating": 8 }`. Ако архивът съдържа други лични данни, извлечете само ratings JSON файла; не качвайте целия Amazon архив, пароли, cookies или токени.

## Етапи на реализация

1. **Модел:** реализирана е отделна `personal_ratings` таблица с уникален нормализиран `imdb_id`, целочислена `rating` в диапазона 1-10 и `updated_at`. Няма задължителен FK към `titles`, така че се пазят и оценки за заглавия извън каталога. EF конфигурацията и миграцията са в [Persistence](../../server/src/MediaDock.Infrastructure/Persistence/).
2. **Импорт:** реализиран е `POST /api/personal-ratings/import` с multipart JSON файл, ограничен до 5 MiB и 50,000 реда. Преди транзакционния merge се валидират IMDb ID, рейтинг 1-10 и съгласувани дубли. Редове с валиден IMDb ID, но липсващ/null рейтинг се пропускат и ID-тата им се връщат в резултата; останалите валидни редове се импортират. Други невалидни данни или повредени файлове не променят данните. Резултатът отчита добавени, обновени, непроменени оценки и грешки по ID. Оригиналният файл не се съхранява.
3. **Филтър в API:** добавя се `excludeRated` с подразбираща се стойност `false` в [OscarContracts.cs](../../server/src/MediaDock.Api/OscarAwards/OscarContracts.cs). [OscarApiService.cs](../../server/src/MediaDock.Api/OscarAwards/OscarApiService.cs) изключва филмите със съвпадащ `OscarFilm.ImdbId` или, ако липсва, `Title.ImdbId`, чрез SQL `NOT EXISTS` по личните оценки **преди** `CountAsync`, подредбата и страниците. Филмите без IMDb ID остават видими. Ако се показва личната оценка, тя е отделно поле от публичната `ImdbRating`.
4. **Интерфейс:** в [SourceSettingsView.tsx](../../web/src/features/sources/SourceSettingsView.tsx) има качване на един JSON файл с резултат и грешка. Oscar catalog checkbox-ът „Скрий оценените от мен“ и личното поле в детайлите остават планирани. Файлът се изпраща само към локалния API, не към IMDb/Amazon.
5. **Документация:** при реализация се актуализират [API договорите](API_CONTRACTS.md), [моделът](DATA_CONTRACTS.md), [статусът](IMPLEMENTATION_STATUS.md) и [оперативната сигурност](SECURITY_AND_OPERATIONS.md). API за качване остава само в съществуващата доверена мрежова граница; преди достъп от интернет е нужна отделна автентикация и оценка на сигурността.

## Приемане и проверки

- Parser unit тестовете и PostgreSQL API тестът покриват повторен импорт, променени/непроменени оценки, нови ID и отказ на невалиден файл без промяна на предишните записи.
- [OscarApiTests.cs](../../server/tests/MediaDock.IntegrationTests/OscarApiTests.cs) проверява включен/изключен филтър, двата възможни IMDb ID източника, липсващ ID, комбинирани филтри и коректни `totalCount`/`totalPages`. Отделен интеграционен тест покрива миграцията и транзакционния импорт в PostgreSQL container.
- [SourceSettingsView.test.tsx](../../web/src/features/sources/SourceSettingsView.test.tsx) покрива качването и статуса на merge-а. Изпълняват се фокусирани .NET unit/integration тестове и в `web` `npm run test -- SourceSettingsView.test.tsx` и `npm run build`.
- Не се включва автоматична синхронизация през публичния IMDb URL. Ако по-късно бъде потвърден разрешен API за лични оценки, негов адаптер трябва да използва същата проверена транзакционна синхронизация; тогава могат да се добавят ръчен бутон и ежедневен график.