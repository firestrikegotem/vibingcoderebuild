-- Avatar thumbnail the way this RCC build was built to do it.
--
-- The job does not insert the avatar's assets itself. It hands RCC the website's
-- CharacterFetch URL, the same approach as Roblox's own 2016 Avatar thumbnail script:
-- one request returns the body colours plus every worn asset id, and RCC loads and
-- assembles the character with its own code path. Doing the asset loads by hand is what
-- produced "asset load fail ... Temp read failed" and took the process down with it.
local jobId = "InsertJobIdHere";
local userId = 65789275746246;
local baseURL = "http://economy-simulator.org";
local uploadURL = "UPLOAD_URL_HERE";
local x, y = _X_RES_, _Y_RES_;
local fileExtension = 'png';

local ContentProvider = game:GetService("ContentProvider")
local ScriptContext = game:GetService("ScriptContext")
local Players = game:GetService("Players")
local HttpService = game:GetService("HttpService")
local ThumbnailGenerator = game:GetService("ThumbnailGenerator")

HttpService.HttpEnabled = true
ScriptContext.ScriptsDisabled = true
game:GetService("StarterGui"):SetCoreGuiEnabled(Enum.CoreGuiType.All, false)
pcall(function() ContentProvider:SetBaseUrl(baseURL) end)
-- 3 is the software GL backend this build ships; 2 and 4 kill the process on the click.
ThumbnailGenerator.GraphicsMode = 3
print('[debug] graphics mode', ThumbnailGenerator.GraphicsMode)

local appearance = baseURL .. "/Asset/CharacterFetch.ashx?userId=" .. tostring(userId)
local player = Players:CreateLocalPlayer(userId)
player.CharacterAppearance = appearance
player:LoadCharacter(false)
print('[debug] loaded character from', appearance)

-- Gear is shown by raising the arm, as the official script does.
if player.Character then
    for _, child in pairs(player.Character:GetChildren()) do
        if child:IsA("Tool") then
            player.Character.Torso["Right Shoulder"].CurrentAngle = math.rad(90)
            break
        end
    end
end

-- The official 2016 script calls Click with five arguments, but this binary also returns
-- "invalid string position" for that form, so accept whichever signature produces a PNG.
-- Click reports failure by returning an error string instead of throwing.
local function tryClick(args)
    local ok, result = pcall(function()
        if args == 5 then
            return ThumbnailGenerator:Click(fileExtension, x, y, true, true)
        end
        return ThumbnailGenerator:Click(fileExtension, x, y, true)
    end)
    if ok and type(result) == 'string' and string.sub(result, 1, 5) == 'iVBOR' then
        print('[debug] render ok with', args, 'arguments, bytes', string.len(result))
        return result
    end
    print('[debug] click with', args, 'arguments returned', ok, tostring(result))
    return nil
end

local avatarEncoded = nil
for attempt = 1, 3 do
    avatarEncoded = tryClick(4) or tryClick(5)
    if avatarEncoded ~= nil then break end
    wait(0.5)
end

if avatarEncoded == nil then
    print('[warn] RCC produced no image for this avatar')
    return
end

print('[debug] [player/thumbnail] send post request containing avatar')
local payload = HttpService:JSONEncode({
    ['thumbnail'] = avatarEncoded,
    ['userId'] = userId,
    ['accessKey'] = "AccessKey",
    ['type'] = "PlayerThumbnail",
    ['jobId'] = jobId,
})
-- Park the image on the job as well: some builds refuse outbound HTTP entirely, and the
-- game server can read this value back over SOAP instead of relying on the post.
pcall(function()
    _G.__bloxynThumbnail = payload
    local holder = game:GetService("ServerStorage"):FindFirstChild("__BloxynThumbnail")
    if holder == nil then
        holder = Instance.new("StringValue")
        holder.Name = "__BloxynThumbnail"
        holder.Parent = game:GetService("ServerStorage")
    end
    holder.Value = payload
end)
local ok, data = pcall(function()
    return HttpService:PostAsync(uploadURL, payload, Enum.HttpContentType.TextPlain)
end)
print('[debug] post over', ok, data)
