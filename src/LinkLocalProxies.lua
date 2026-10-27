-- Link Local Proxies — скрипт DJI Proxy Linker для DaVinci Resolve.
-- https://github.com/blemb666/dji-proxy-linker
--
-- Оригиналы лежат на сервере, а их прокси программа скачала на этот компьютер,
-- повторив структуру папок. Скрипт проходит по всем клипам Media Pool текущего проекта
-- и привязывает к каждому клипу с сервера его локальный прокси:
--   \\server\FastMotion\ZERO_U6\1 смена\FX30\C0001.MP4
--   → D:\Proxy\ZERO_U6\1 смена\FX30\Proxy\C0001.MP4
--
-- Запуск: Workspace → Scripts → Link Local Proxies. Итог — в Workspace → Console.
-- Файл перезаписывается программой при каждой синхронизации — не правьте его вручную.

local MAPPINGS = {
--@@MAPPINGS@@
}

local function norm(path)
    return (path:gsub("/", "\\"):gsub("\\+$", ""))
end

-- Сопоставление пути клипа с локальным прокси; nil — клип не с этого сервера
local function proxyFor(path)
    path = norm(path)
    for _, m in ipairs(MAPPINGS) do
        local root = norm(m[1])
        local head, rest = path:sub(1, #root), path:sub(#root + 1)
        if head:lower() == root:lower() and rest:sub(1, 1) == "\\" then
            local dir, name = rest:match("^(.*)\\([^\\]+)$")
            if dir then return norm(m[2]) .. dir .. "\\Proxy\\" .. name end
        end
    end
    return nil
end

local resolve = resolve or Resolve()
local project = resolve:GetProjectManager():GetCurrentProject()
if not project then
    print("Link Local Proxies: сначала откройте проект")
    return
end

local linked, missing, other, failed = 0, 0, 0, 0

-- В разных версиях Resolve ключ пишется "File Path" или "File path"
local function filePath(clip)
    for _, key in ipairs({ "File Path", "File path" }) do
        local value = clip:GetClipProperty(key)
        if type(value) == "string" and value ~= "" then return value end
    end
    local all = clip:GetClipProperty()
    if type(all) == "table" then return all["File Path"] or all["File path"] end
    return nil
end

local function walk(folder)
    for _, clip in pairs(folder:GetClipList() or {}) do
        local path = filePath(clip)
        if path then
            local proxy = proxyFor(path)
            if not proxy then
                other = other + 1
            else
                local ok, result = pcall(function() return clip:LinkProxyMedia(proxy) end)
                if not ok then
                    failed = failed + 1
                    print("  ошибка API: " .. tostring(result))
                elseif result then
                    linked = linked + 1
                else
                    missing = missing + 1
                    print("  нет прокси: " .. proxy)
                end
            end
        end
    end
    for _, sub in pairs(folder:GetSubFolderList() or {}) do walk(sub) end
end

walk(project:GetMediaPool():GetRootFolder())

local summary = string.format(
    "Привязано прокси: %d\nНе найдено на компьютере: %d\nКлипы не с сервера: %d",
    linked, missing, other)
if failed > 0 then
    summary = summary .. string.format("\nОшибок API: %d — обновите DaVinci Resolve", failed)
end
print("Link Local Proxies\n" .. summary)
if #MAPPINGS == 0 then
    print("Нет ни одной пары папок — сначала синхронизируйте прокси в DJI Proxy Linker")
end

-- Окно с итогом (в бесплатной версии Resolve может быть недоступно — тогда только консоль)
pcall(function()
    local ui = fu.UIManager
    local disp = bmd.UIDispatcher(ui)
    local win = disp:AddWindow({ ID = "LLP", WindowTitle = "Link Local Proxies", Geometry = { 400, 300, 380, 150 } },
        ui:VGroup {
            ui:Label { Text = summary, WordWrap = true },
            ui:Button { ID = "OK", Text = "OK" },
        })
    function win.On.OK.Clicked() disp:ExitLoop() end
    function win.On.LLP.Close() disp:ExitLoop() end
    win:Show()
    disp:RunLoop()
    win:Hide()
end)
