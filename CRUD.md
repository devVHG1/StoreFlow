# Каталог StoreFlow

Categories і Products підтримують GET списку, GET за id, POST, PUT і DELETE:

- `/api/categories`
- `/api/products`

Список Products повертає об’єкт із items, page, pageSize, totalCount і totalPages.
Пошук та фільтри: /api/products?search=книга&minPrice=100&sortBy=price&page=1&pageSize=20.

POST повертає 201 і Location, PUT та DELETE — 204.
Помилкові дані повертають 400, відсутній запис — 404.
Категорію з товарами або товар у замовленні видалити не можна: 409.

DTO, інтерфейси й сервіси містяться в Application. Репозиторій EF Core —
в Infrastructure. Контролери отримують сервіси через DI.

Приклад створення категорії:

```json
{"name":"Книги","description":"Навчальні видання"}
```

Приклад створення товару (підставте id створеної категорії):

```json
{"name":"Підручник","description":"C#","price":350,"stockQuantity":10,"categoryId":1}
```

Запуск зі Swagger:

```powershell
dotnet run --project StoreFlow.Аpi --launch-profile http
```

Swagger: http://localhost:5276/swagger

Перевірка API (PowerShell 7, із запущеним API):

```powershell
$env:STOREFLOW_ACCESS_TOKEN = Read-Host 'Токен Admin або Manager'
pwsh -NoProfile -File scripts/Test-CatalogApi.ps1 -BaseUrl http://localhost:5276
```

Скрипт створює власні тимчасові записи й прибирає їх після перевірки.
Використовується наявна PostgreSQL і наявна схема без нових міграцій.

Оновлення товару від 29.10.2026: спочатку GET за id, потім PUT із отриманим `version`.
Застаріла версія повертає 409. Якщо змінюєте залишок, передайте також непорожнє `stockChangeReason` до 500 символів.
Рухи складу доступні персоналу через `/api/stock-movements`; докладний контракт наведений у README.
