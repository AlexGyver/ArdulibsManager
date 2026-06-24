# ArdulibsManager
Кастомный менеджер Arduino-библиотек для Windows. Работает напрямую с GitHub, а не с экосистемой Arduino, которая заблокировала доступ пользователям из России

Скачать свежую версию:
- [Windows 64 bit](https://github.com/AlexGyver/ArdulibsManager/releases/latest/download/ArdulibsManager-x64.exe)
- [Windows 32 bit](https://github.com/AlexGyver/ArdulibsManager/releases/latest/download/ArdulibsManager-x86.exe)

Возможности:
- Работа напрямую с GitHub-списком библиотек в обход Arduino
- Рекурсивная установка зависимостей
- Сканирование и менеджмент установленных библиотек
- Проверка обновлений
- Backup перед заменой в `%LOCALAPPDATA%\ArdulibsManager\Backups`

## GitHub token
Токен необязателен. Он нужен только для повышения лимитов GitHub API. Когда может понадобиться:

- Установлено много библиотек
- Часто проверяются обновления
- Часто открываются dropdown-ы версий
- Часто ставятся библиотеки с зависимостями
- GitHub начал отвечать ошибкой rate limit

Без авторизации GitHub REST API обычно ограничен 60 запросами в час на IP. С Personal Access Token лимит для пользователя обычно выше — 5000 запросов в час.

Где взять токен:

1. Открой GitHub.
2. Перейди в **Settings** → **Developer settings** → **Personal access tokens**.
3. Лучше создать **Fine-grained token**.
4. Для публичных репозиториев специальные права обычно не нужны. Если GitHub требует выбрать permissions, выбирай минимальные read-only permissions.
5. Скопируй токен и вставь его в настройках приложения.

Официальная документация GitHub:

- Managing your personal access tokens: https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens
- Rate limits for the REST API: https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api

## Ограничения
- Версии берутся из GitHub tags
- Не все репозитории имеют аккуратные tags/releases
- Если `library.properties` не содержит GitHub URL, обновления такой библиотеки могут не проверяться
- Если dependency name сильно отличается от repo name, автоматический поиск зависимости может не сработать