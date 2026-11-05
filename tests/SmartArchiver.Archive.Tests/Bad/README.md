Сюди кладуться справжні `bad_*.arc` і `expected_failures.json` від викладача (файли копіюються в теку тестів).
Формат JSON: `{"bad_magic.arc": "E_MAGIC", ...}`. Пароль до них: змінна `TEST_ARCHIVE_PASSWORD`.
Власні пошкоджені архіви `BadArchiveTests` будує сам і не потребує цієї теки.
