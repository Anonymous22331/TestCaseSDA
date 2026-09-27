# TestCaseSDA

REST API на ASP.NET Core .NET 10.

## Запуск

Нужен Docker с Docker Compose. В корне проекта выполните:

```bash
docker compose up
```

Дождитесь сборки и запуска API. При первом запуске загрузятся образы и зависимости.

- Swagger: http://localhost:8090/api/swagger
- pgAdmin: http://localhost:8080 — доступ к БД настроен, пароль вводить не нужно.

## Проверка

В Swagger откройте `POST /api/process`, нажмите **Try it out** и вставьте JSON из
`json_payload_1.txt` или `json_payload_2.txt`. Примеры ответов — `json_result_1.txt` и `json_result_2.txt`.

В pgAdmin откройте `Servers → TestCaseSDA PostgreSQL → Databases → testcase → Schemas → public → Tables → elements`
и выберите **View/Edit Data → All Rows**, чтобы увидеть сохранённые элементы.

Остановка: `docker compose down`. Данные БД сохраняются между запусками.
