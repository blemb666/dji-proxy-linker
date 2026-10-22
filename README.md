<div align="center">

<img src="docs/icon.png" width="96" alt="DJI Proxy Linker">

# DJI Proxy Linker

**Готовые прокси с DJI Pocket / Osmo / дронов — в DaVinci Resolve за один клик**

[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows)
![Size](https://img.shields.io/badge/size-~100%20KB-success)
[![Telegram](https://img.shields.io/badge/Telegram-Adam%20Editing-26A5E4?logo=telegram)](https://t.me/adam_editing_page)

<img src="docs/screenshot.png" width="560" alt="Скриншот">

[Русский](#зачем) · [English](#english)

</div>

## Зачем

Камеры DJI (Pocket 3, Osmo Action, Avata, Mini и др.) **уже пишут прокси** рядом с каждым видео — это файлы `.LRF`.
Но DaVinci Resolve их не понимает из-за расширения. Если переименовать `.LRF` → `.MP4` и положить в подпапку `Proxy`,
Resolve **сам подцепит их как прокси** — и не нужно тратить время на генерацию своих.

DJI Proxy Linker делает это автоматически для любого количества папок:

```
DJI_001\
├── DJI_20261022121502_0041_D.MP4        ← оригинал
├── DJI_20261022121502_0041_D.LRF   ─┐
└── Proxy\                           │  перенос + переименование
    └── DJI_20261022121502_0041_D.MP4 ◄┘  ← прокси для Resolve
```

## Возможности

- **Перетащите** папки (хоть всю карту `DCIM`) в окно или на значок `.exe` — подпапки обрабатываются рекурсивно
- **Три режима:** перенести (мгновенно, без лишнего места) · скопировать (LRF остаются на месте) · откатить обратно в `.LRF`
- Расширение прокси совпадает с исходником (`.MP4` / `.MOV`), чтобы Resolve точно сопоставил имена
- Повторный запуск безопасен: уже готовые прокси пропускаются
- Один файл ~100 КБ, без установки — работает на встроенном в Windows .NET Framework 4.8

## Как пользоваться

1. Скачайте `DjiProxyLinker.exe` со страницы [Releases](https://github.com/blemb666/dji-proxy-linker/releases) (или [соберите сами](#сборка)).
2. Скопируйте видео с карты на диск.
3. Перетащите папку с видео в окно программы и нажмите **«Создать прокси»**.
4. Импортируйте папку в DaVinci Resolve — прокси подтянутся автоматически.
   Если клипы уже были в проекте: **Media Pool → ПКМ по клипам → Link Proxy Media** и укажите папку.

> Windows может показать «Система Windows защитила ваш компьютер», потому что exe не подписан.
> Нажмите **Подробнее → Выполнить в любом случае**. Исходный код целиком открыт в этом репозитории.

**Горячие клавиши:** `Ctrl+O` — выбрать папки · `Enter` — запустить · `Esc` — очистить.

### Через меню «Отправить»

Нажмите `Win+R`, введите `shell:sendto` и положите туда ярлык на `DjiProxyLinker.exe`.
Теперь: ПКМ по папке → **Отправить** → DjiProxyLinker.

### Командная строка

```
DjiProxyLinker.exe [--copy | --undo] [--run] <папки или файлы...>
```

| Флаг | Что делает |
|---|---|
| `--copy` | копировать LRF вместо переноса |
| `--undo` | вернуть `Proxy\*.MP4` обратно в `*.LRF` |
| `--run` | сразу запустить обработку, без нажатия кнопки |

## Сборка

Нужна только Windows 10/11 — компилятор C# уже встроен в .NET Framework.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Готовый файл появится в `dist\DjiProxyLinker.exe`.

| Файл | Что внутри |
|---|---|
| `src/Linker.cs` | поиск LRF и перенос/копирование/откат |
| `src/App.cs` | окно, перетаскивание, журнал, аргументы командной строки |
| `src/MainWindow.xaml` | разметка и стили интерфейса (WPF) |
| `src/Icon.xaml` | векторная иконка, при сборке рендерится в `.ico` |
| `src/FolderPicker.cs` | современный диалог выбора нескольких папок |

## Поддержать

Проект развивается при поддержке **Adam Editing** — монтаж, цветокор и всё про DaVinci Resolve:

<a href="https://t.me/adam_editing_page"><img src="https://img.shields.io/badge/Telegram-@adam__editing__page-26A5E4?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram"></a>

Подписывайтесь, ставьте ⭐ репозиторию и присылайте идеи в [Issues](https://github.com/blemb666/dji-proxy-linker/issues).

---

## English

**DJI Proxy Linker** turns the `.LRF` low-res files that DJI cameras record next to every clip into proxies DaVinci Resolve picks up automatically.
It moves (or copies) each `NAME.LRF` to `Proxy\NAME.MP4` next to the original, so Resolve links it on import — no proxy rendering needed.

- Drag & drop folders onto the window or the `.exe`; subfolders are processed recursively
- Modes: move (instant), copy, undo
- Single ~100 KB executable, no installer, runs on the .NET Framework 4.8 that ships with Windows 10/11
- Build: `powershell -ExecutionPolicy Bypass -File build.ps1`

Sponsored by [Adam Editing on Telegram](https://t.me/adam_editing_page).

## Лицензия

[Apache License 2.0](LICENSE). Проект не связан с DJI и Blackmagic Design; названия принадлежат их владельцам.
