# Как включить подпись релизов (SignPath Foundation)

Сборка уже готова к подписи: [release.yml](../.github/workflows/release.yml) подписывает сначала
`DjiProxyLinker.exe`, затем собирает из него установщик и подписывает его. Пока в репозитории
не заданы настройки SignPath, эти шаги пропускаются и релиз выходит неподписанным.

## 1. Подготовка

- Разблокируйте GitHub Actions (Settings → Billing and plans): SignPath подписывает только файлы,
  собранные в GitHub Actions на серверах GitHub.
- Включите двухфакторную аутентификацию на GitHub — это обязательное условие SignPath.
- Выпустите хотя бы один релиз через Actions (`git tag vX.Y.Z && git push origin vX.Y.Z`):
  SignPath требует, чтобы проект уже распространялся в том виде, в котором будет подписываться.

## 2. Заявка

Подайте заявку на [signpath.org/apply](https://signpath.org/apply). Условия — на
[signpath.org/terms](https://signpath.org/terms). Раздел «Code signing policy» в README уже
соответствует их требованиям.

## 3. Настройка в SignPath (после одобрения)

1. Включите 2FA в аккаунте SignPath.
2. **Trusted build systems** → добавьте предопределённую систему **GitHub.com** и привяжите её к проекту.
3. Установите **SignPath GitHub App** и дайте ей доступ к репозиторию `dji-proxy-linker`.
4. В проекте создайте две **Artifact configurations** со slug-ами ровно `exe` и `installer` —
   содержимое скопируйте из [.signpath/artifact-configurations](../.signpath/artifact-configurations).
5. Проверьте slug-и проекта и политики подписи (по умолчанию workflow ожидает
   `dji-proxy-linker` и `release-signing`).
6. Создайте **API token** для пользователя, который отправляет запросы на подпись (роль Submitter).

## 4. Настройка в GitHub

Settings → Secrets and variables → Actions:

| Тип | Имя | Значение |
|---|---|---|
| Secret | `SIGNPATH_API_TOKEN` | API token из SignPath |
| Variable | `SIGNPATH_ORGANIZATION_ID` | ID организации из SignPath |
| Variable | `SIGNPATH_PROJECT_SLUG` | только если slug проекта не `dji-proxy-linker` |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | только если политика не `release-signing` |

## 5. Выпуск подписанного релиза

```
git tag v1.2.0
git push origin v1.2.0
```

Workflow отправит два запроса на подпись (exe, затем установщик). Каждый нужно **одобрить
вручную** в SignPath в течение часа, иначе сборка завершится по таймауту. После подписи шаг
«Check signatures» проверит, что оба файла подписаны, и опубликует релиз.

Не подписывается деинсталлятор `unins000.exe` внутри установщика: его создаёт Inno Setup уже при
установке, и SmartScreen его не проверяет (он не скачивается из интернета).
