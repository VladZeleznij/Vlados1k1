-- MARCH ROLE PLAY | Local launcher-to-MTA handoff.
-- SECURITY: this is ONLY for a client and MTA server on the SAME computer.
-- No web API or passwords exposed to the internet; do not allow remote IPs.
-- Works with native MTA accounts (getAccount/addAccount/logIn). If march_auth
-- uses its own SQLite accounts, integrate into march_auth instead of using
-- a second independent account store.
local endpoint = "http://127.0.0.1:28745"
local attempts = {}
local COOLDOWN_MS = 4500

local function respond(id, ok, message)
    if type(id) ~= "string" or not id:match("^[a-fA-F0-9]+$") or #id ~= 32 then return end
    local payload = toJSON({ id = id, ok = ok, message = message }, true)
    fetchRemote(endpoint .. "/result", {
        method = "POST",
        postData = payload,
        headers = { ["Content-Type"] = "application/json" },
        connectionAttempts = 1,
        connectTimeout = 2500,
        queueName = "march_launcher_auth"
    }, function() end)
end

local function validName(s)
    return type(s) == "string" and #s >= 3 and #s <= 24
        and s:match("^[A-Za-z0-9_]+$") ~= nil
end

local function validPassword(s)
    return type(s) == "string" and #s >= 8 and #s <= 128
end

local function apply(player, payload)
    if not isElement(player) or getElementType(player) ~= "player" then return end
    if getPlayerIP(player) ~= "127.0.0.1" and getPlayerIP(player) ~= "::1" then return end
    if not isGuestAccount(getPlayerAccount(player)) then return end
    if type(payload) ~= "table" or payload.ok ~= true then return end
    local id, mode, name, password = payload.id, payload.mode, payload.username, payload.password
    if type(id) ~= "string" or #id ~= 32 then return end
    if not validName(name) or not validPassword(password) or
        (mode ~= "login" and mode ~= "register") then
        respond(id, false, "Неверный логин или пароль.")
        return
    end

    local account
    if mode == "register" then
        if getAccount(name) then
            respond(id, false, "Аккаунт с таким логином уже существует.")
            return
        end
        account = addAccount(name, password)
        if not account then
            respond(id, false, "Не удалось создать аккаунт. Проверь ACL.")
            return
        end
    else
        account = getAccount(name, password)
        if not account then
            respond(id, false, "Неверный логин или пароль, либо аккаунт хранится в другой базе.")
            return
        end
    end

    if not isElement(player) then return end
    if not logIn(player, account, password) then
        respond(id, false, "Сервер отклонил вход. Проверь ACL ресурса.")
        return
    end
    -- Standard onPlayerLogin fires for existing systems such as ACL, HUD and apartments.
    setElementData(player, "march:launcherAuth", true, false)
    triggerClientEvent(player, "march_launcher_auth:authenticated", resourceRoot, name)
    respond(id, true, mode == "register"
        and "Регистрация выполнена. Игровой аккаунт открыт."
        or "Вход выполнен. Повторный пароль не нужен.")
    outputServerLog("[MARCH Launcher] Local account auth: " .. name .. " (" .. mode .. ")")
end

addEventHandler("onPlayerJoin", root, function()
    local player = source
    if getPlayerIP(player) ~= "127.0.0.1" and getPlayerIP(player) ~= "::1" then return end

    local now = getTickCount()
    if attempts[player] and now - attempts[player] < COOLDOWN_MS then return end
    attempts[player] = now

    -- Let ordinary MTA join initialization complete before applying login.
    setTimer(function()
        if not isElement(player) or not isGuestAccount(getPlayerAccount(player)) then return end
        fetchRemote(endpoint .. "/pending", {
            method = "GET",
            connectionAttempts = 1,
            connectTimeout = 3000,
            queueName = "march_launcher_auth"
        }, function(data, status)
            if not isElement(player) or status ~= 0 or type(data) ~= "string" then return end
            local payload = fromJSON(data)
            apply(player, payload)
        end)
    end, 1200, 1)
end)

addEventHandler("onPlayerQuit", root, function() attempts[source] = nil end)
