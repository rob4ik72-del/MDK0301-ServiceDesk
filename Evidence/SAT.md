# Протокол SAT — стенд

Дата: 2026-11-09 17:19:43 UTC

| Сценарий | Результат | Ответ |
| --- | --- | --- |
| GET /health | PASS | HTTP 200 |
| GET /api/v1/tickets | PASS | HTTP 200 |
| GET /api/v2/tickets | PASS | HTTP 200 |
| GET /api/v1/tickets/2147483647 | PASS | HTTP 404 |

Пройдено: 4/4. Согласование заказчиком проводится отдельно.
