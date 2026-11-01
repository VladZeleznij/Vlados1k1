-- Optional notification for a local launcher-authenticated player.
-- The existing march_auth client UI should hide itself once server login
-- completes (see README integration instructions).
addEvent("march_launcher_auth:authenticated", true)
addEventHandler("march_launcher_auth:authenticated", resourceRoot, function(username)
    if type(username) ~= "string" then return end
    outputChatBox("[MARCH] Добро пожаловать, " .. username .. "!", 70, 225, 215)
end)
