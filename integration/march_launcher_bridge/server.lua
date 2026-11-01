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
    -- MTA may wrap tables in an outer JSON array; the receiver accepts an object.
    if payload and payload:sub(1, 1) == "[" then
        payload = payload:sub(2, -2)
    end
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
        respond(id, false, "invalid_credentials")
        return
    end

    local account
    if mode == "register" then
        if getAccount(name) then
            respond(id, false, "username_exists")
            return
        end
        account = addAccount(name, password)
        if not account then
            respond(id, false, "missing_acl")
            return
        end
    else
        account = getAccount(name, password)
        if not account then
            respond(id, false, "invalid_credentials_or_external_db")
            return
        end
    end

    if not isElement(player) then return end
    if not logIn(player, account, password) then
        respond(id, false, "auth_denied")
        return
    end
    -- Standard onPlayerLogin fires for existing systems such as ACL, HUD and apartments.
    setElementData(player, "march:launcherAuth", true)
    triggerClientEvent(player, "march_launcher_auth:authenticated", resourceRoot, name)
    respond(id, true, mode == "register"
        and "registration_success"
        or "login_success")
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
        }, function(data, responseInfo)
            local success = (type(responseInfo) == "table" and responseInfo.success == true)
                or (type(responseInfo) == "number" and responseInfo == 0)
            if not isElement(player) or not success or type(data) ~= "string" then return end
            local payload = fromJSON(data)
            -- A nested fetchRemote from inside this callback is unsupported by MTA.
            setTimer(function()
                if isElement(player) then apply(player, payload) end
            end, 50, 1)
        end)
    end, 1200, 1)
end)

addEventHandler("onPlayerQuit", root, function() attempts[source] = nil end)
