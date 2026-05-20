# ClientiX

> SaaS-платформа для самозанятых бьюти-мастеров с Telegram-ботами.
> Каждый мастер получает собственного бота для записи клиентов с полным циклом: услуги, портфолио, расписание, бронирование, напоминания, статистика.

🌐 **Production:** [clientix-assist.ru](https://clientix-assist.ru)
🤖 **Главный бот:** [@cl1ent1x_bot](https://t.me/cl1ent1x_bot)

---

## Возможности продукта

### Для мастера
- Личный кабинет в Telegram через главный бот
- Подключение собственного бота клиентов через @BotFather
- Каталог услуг с ценами и длительностью
- Портфолио (фото работ)
- Гибкое расписание: шаблон недели + исключения на конкретные даты
- Календарь с раскраской (рабочий/выходной/особый/прошедший)
- Настраиваемые горизонт записи (7/14/30/60 дней) и часовой пояс (11 поясов РФ)
- Управление записями: создание, перенос, отмена, добавление/удаление услуг
- Завершение записи (выполнена / клиент не пришёл)
- Настраиваемые напоминания клиентам (за 24 ч + 1/3/6/12 ч)
- Статистика: записи и доход за день / 7 дней / 30 дней
- Подписка через ЮKassa с триал-периодом

### Для клиента
- Запись через бот мастера в 3 шага: услуга → дата → время
- Календарь с раскраской свободных дат
- Просмотр своих записей
- Перенос и отмена записей
- Добавление и удаление услуг в существующей записи
- Автоматические напоминания
- Опциональная проверка подписки на TG-канал мастера

---

## Архитектура

### Стек
- **Backend:** .NET 8, C#, Clean Architecture
- **БД:** PostgreSQL 16
- **Кэш и FSM:** Redis 7
- **Message broker:** RabbitMQ 3.13 (заготовка под микросервисы)
- **Telegram:** Telegram.Bot 22.x (long polling)
- **Платежи:** Yandex.Checkout.V3 (ЮKassa)
- **Контейнеризация:** Docker Compose, Caddy 2 (HTTPS через Let's Encrypt)
- **Хостинг:** Yandex Cloud (Ubuntu 22.04)

### Слои
```
ClientiX.Domain          — Entities, value objects
ClientiX.Application     — (in progress) Use cases, interfaces
ClientiX.Infrastructure  — EF Core, Repositories, ЮKassa, Redis, BookingSlotService
ClientiX.BotGateway      — Telegram polling, FSM, MasterBotManager, webhook ЮKassa
```

### Мульти-бот
В одном процессе `BotGateway` работают:
1. **Главный бот** `@cl1ent1x_bot` через `TelegramPollingService` — управление кабинетом мастера
2. **Боты мастеров** через `MasterBotManager` — динамическое подключение/отключение long polling-ов для каждого мастера

### Защита от race condition
В таблице `bookings` — уникальный частичный индекс:
```sql
CREATE UNIQUE INDEX idx_bookings_no_overlap
ON bookings ("UserId", "StartsAt")
WHERE "Status" IN ('pending', 'confirmed');
```
Это гарантирует, что два клиента не смогут одновременно забронировать один слот.

### Безопасность
- Токены ботов мастеров шифруются через ASP.NET Data Protection
- Rate-limit на бронирование (5 операций/мин, max 3 активных записи)
- Webhook ЮKassa с проверкой подписи и идемпотентностью
- HTTPS через Let's Encrypt (Caddy)

---

## Локальный запуск

### Требования
- .NET 8 SDK
- Docker Desktop
- PowerShell или bash

### Шаги
```powershell
# 1. Склонировать
git clone https://github.com/ucsmdeeeee/Clientix
cd Clientix

# 2. Скопировать пример конфига
cp src/ClientiX.BotGateway/appsettings.Development.json.example `
   src/ClientiX.BotGateway/appsettings.Development.json
# Отредактируй: вставь токен бота, тестовые ключи ЮKassa

# 3. Запустить инфраструктуру (Postgres, Redis, RabbitMQ)
docker compose up -d postgres redis rabbitmq

# 4. Применить миграции
dotnet ef database update --project src/ClientiX.Infrastructure --startup-project src/ClientiX.BotGateway

# 5. Запустить BotGateway
dotnet run --project src/ClientiX.BotGateway
```

### Создание миграций
```powershell
dotnet ef migrations add НазваниеМиграции `
    --project src/ClientiX.Infrastructure `
    --startup-project src/ClientiX.BotGateway `
    --output-dir Persistence/Migrations
```

---

## Production-деплой

### Архитектура на ВМ
```
┌───────────────────────────────────────┐
│ Yandex Cloud VM (Ubuntu 22.04)        │
│                                       │
│  ┌─────────────────────────────────┐  │
│  │ Docker Compose                  │  │
│  │  ┌────────┐  ┌──────────────┐   │  │
│  │  │ Caddy  │→│ BotGateway   │   │  │
│  │  │ :443   │  │ :5000        │   │  │
│  │  └────────┘  └──────────────┘   │  │
│  │       │            │            │  │
│  │   Let's       ┌────┴─────┐      │  │
│  │   Encrypt     │          │      │  │
│  │           Postgres   Redis      │  │
│  │                                 │  │
│  │           RabbitMQ              │  │
│  └─────────────────────────────────┘  │
│                                       │
└───────────────────────────────────────┘
   ↑
   clientix-assist.ru (HTTPS)
```

### Деплой обновлений
```bash
ssh clientix@46.21.246.30
cd ~/Clientix
git pull
docker compose up -d --build botgateway
docker logs clientix-botgateway --tail 20
```

Автомиграции применяются при старте контейнера.

---

## Резервное копирование

### Автоматические бэкапы
Cron на ВМ создаёт `pg_dump` каждые 6 часов в `/var/backups/clientix/`. Хранятся последние 14 файлов (~3.5 суток истории).

Скрипт: `/home/clientix/clientix-backup.sh`

Логи: `/var/log/clientix-backup.log`

### Ручной бэкап
```bash
/home/clientix/clientix-backup.sh
```

### Восстановление из бэкапа
```bash
# Остановить бот, чтобы избежать конфликта записи
docker compose stop botgateway

# Восстановить (укажи нужный файл)
gunzip < /var/backups/clientix/clientix-YYYYMMDD-HHMMSS.sql.gz | \
    docker exec -i clientix-postgres psql -U clientix -d clientix

# Запустить обратно
docker compose start botgateway
```

---

## Структура репозитория
```
Clientix/
├── src/
│   ├── ClientiX.Domain/          # Entities
│   ├── ClientiX.Application/     # Use cases (в развитии)
│   ├── ClientiX.Infrastructure/  # EF Core, Repositories, Services
│   └── ClientiX.BotGateway/      # Telegram, Webhook, FSM
├── docker-compose.yml
├── Dockerfile
├── Caddyfile
└── README.md
```

---

## Roadmap

### Реализовано (MVP)
- [x] Полный цикл записи (создание/перенос/отмена/добавление-удаление услуг)
- [x] Календари с раскраской
- [x] Мульти-таймзоны (11 поясов РФ)
- [x] Напоминания клиентам
- [x] ЮKassa в проде
- [x] Rate-limit и базовая безопасность
- [x] Автоматические бэкапы

### Планируется
- [ ] Завершение архитектурного рефакторинга на полноценные микросервисы
- [ ] Unit + integration тесты
- [ ] Лендинг проекта
- [ ] Веб-аналитика для мастеров
- [ ] Чат «клиент ↔ мастер» через бот
- [ ] Отзывы клиентов после визита
- [ ] Реферальная программа для клиентов
- [ ] Многоязычность (английский)

---

## Дипломный проект

Этот проект разрабатывается как дипломная работа студента **Снопова Даниила Руслановича**, группа 4П3, специальность **09.02.07 «Информационные системы и программирование»**, ITHub Колледж.

GitHub: [@ucsmdeeeee](https://github.com/ucsmdeeeee)
Telegram: [@ucsmdeeeee](https://t.me/ucsmdeeeee)

---

## Лицензия

Проприетарный коммерческий проект. Все права защищены © 2026.
