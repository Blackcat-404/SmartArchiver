Сюди кладуться еталонні архіви викладача `ref_*.arc` (багатотомні як `ref_06_two_volumes.part1.arc`, …) разом із `ref_*.json`.
Тест `AcceptanceC1Tests.C1_12_ReferenceArchives_FromTheTeacher_Unpack` розпаковує кожен архів (пароль `algo2026` з FORMAT_SPEC_P2,
розділ 8, або змінна `TEST_ARCHIVE_PASSWORD`), перевіряє HMAC і CRC32 кожного файлу та звіряє імена й розміри з `.json`.
Поки папка порожня, тест нічого не перевіряє.
