## API

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/health` | Жив ли процесс; не проверяет БД |
| POST | `/api/admin/login` | JSON `{login,password}`, HttpOnly cookie |
| POST | `/api/admin/logout` | Удаление cookie |
| GET | `/api/admin/me` | Проверка администратора |
| GET | `/api/products` | Активные товары с variants и previewImage |
| GET | `/api/categories` | Категории по id |
| GET | `/api/colors` | Цвета по id |
| POST | `/api/products/create` | Создание, multipart, только admin |
| PUT | `/api/products/{id}` | Редактирование, multipart, только admin |
| POST | `/api/contact` | JSON `{name,phone,comment,company?}` |

Поля товара: `name`, `slug`, `description`, `categoryId`, `isVariable`, `isActive`, `image?`.
Простой товар: `price`, `colorId`, `sku?`, `inStock?`.
Вариативный: `variants` — JSON-массив `{colorId,price,sku?,inStock?}`, цена строкой, как в текущей форме Nuxt.
Редактирование простого товара без colorId допускает null, как текущий Nuxt handler.
Изображения: PNG/JPEG/WebP/GIF до 10 MB; bucket `products-images` должен существовать и иметь публичное чтение.
Ошибки сохраняют поля `statusCode`, `statusMessage`, `message`, используемые `$fetch`.
