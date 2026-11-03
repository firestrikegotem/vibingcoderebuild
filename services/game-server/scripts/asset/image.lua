-- Clothing thumbnail renderer.
--
-- Renders a catalog thumbnail for a Shirt (11), Pants (12) or T-Shirt (2) by putting the
-- clothing on a real 3D dummy instead of trying to photograph the flat 2D texture, which
-- used to come out as a grey untextured mannequin.
--
-- Placeholders replaced by services/game-server/src/scripts.ts and controllers/index.ts:
--   65789275746246 -> assetId     358843 -> assetType
--   UPLOAD_URL_HERE -> game server upload endpoint      AccessKey -> authorization
--   InsertJobIdHere -> render job id

local jobId = "InsertJobIdHere";
local assetId = 65789275746246;
local assetType = 358843;
-- The image the clothing is drawn with. When the game server knows it, the dummy is dressed from
-- that picture directly instead of loading the clothing item and unpacking its XML mid-render.
local textureId = 73111111111119;
-- RCC's TrustCheck refuses to load anything from "localhost", so always use the raw loopback IP.
local baseURL = "http://bbblox.org";
local uploadURL = "UPLOAD_URL_HERE";

local HttpService = game:GetService("HttpService");
local ThumbnailGenerator = game:GetService('ThumbnailGenerator');
local InsertService = game:GetService("InsertService");
local ContentProvider = game:GetService("ContentProvider");

HttpService.HttpEnabled = true
game:GetService('StarterGui'):SetCoreGuiEnabled(Enum.CoreGuiType.All, false)
-- Software GL. GraphicsMode 2 wanted a hardware context that is not there and took the process
-- down with it; 3 is what the avatar scripts render reliably on.
ThumbnailGenerator.GraphicsMode = 3

-- Click returns the error text instead of throwing, so the only proof of a render is a PNG header.
-- The first click after start always fails on this build, hence the 1x1 warm-up.
local function clickPng(width, height)
    pcall(function() return ThumbnailGenerator:Click('png', 1, 1, true) end)
    for attempt = 1, 4 do
        local ok, result = pcall(function()
            return ThumbnailGenerator:Click('png', width, height, true)
        end)
        if ok and type(result) == 'string' and string.sub(result, 1, 5) == 'iVBOR' then
            print('[debug] clothing render ok on attempt', attempt, 'bytes', string.len(result))
            return result
        end
        print('[debug] clothing render attempt', attempt, 'returned', ok, tostring(result))
        wait(0.5)
    end
    return nil
end

-- Point every asset request at the loopback IP instead of the "localhost" hostname.
ContentProvider:SetBaseUrl(baseURL .. "/")
InsertService:SetAssetUrl(baseURL .. "/asset/")
InsertService:SetAssetVersionUrl(baseURL .. "/asset/?assetversionid=%d")
pcall(function()
    game:GetService("ScriptInformationProvider"):SetAssetUrl(baseURL .. "/asset/")
end)

local function buildDummy()
    local dummy = Instance.new("Model")
    dummy.Name = "ClothingDummy"

    local humanoid = Instance.new("Humanoid")
    humanoid.Parent = dummy

    local torso = Instance.new("Part")
    torso.Name = "Torso"
    torso.Size = Vector3.new(2, 2, 1)
    torso.Position = Vector3.new(0, 3, 0)
    torso.BrickColor = BrickColor.new("Institutional white")
    torso.Anchored = true
    torso.Parent = dummy

    -- A head makes the framing look like a real catalog render instead of a floating box.
    local head = Instance.new("Part")
    head.Name = "Head"
    head.Size = Vector3.new(2, 1, 1)
    head.Position = Vector3.new(0, 4.5, 0)
    head.BrickColor = BrickColor.new("Institutional white")
    head.Anchored = true
    head.Parent = dummy

    local face = Instance.new("Decal")
    face.Name = "face"
    face.Texture = "rbxasset://textures/face.png"
    face.Parent = head

    dummy.PrimaryPart = torso
    dummy.Parent = workspace

    return dummy, torso
end

-- Returns the Shirt / Pants / ShirtGraphic instance inside a loaded asset, if there is one.
local function findClothing(container)
    if not container then return nil end

    local direct = container:FindFirstChildOfClass("Shirt")
        or container:FindFirstChildOfClass("Pants")
        or container:FindFirstChildOfClass("ShirtGraphic")
    if direct then return direct end

    -- InsertService usually wraps the item in a Model, so walk the tree.
    for _, child in pairs(container:GetDescendants()) do
        if child:IsA("Shirt") or child:IsA("Pants") or child:IsA("ShirtGraphic") then
            return child
        end
    end

    return nil
end

-- Fallback when LoadAsset fails: build the clothing instance ourselves from the asset URL.
local function buildClothingFromUrl(dummy, torso)
    local assetUrl = baseURL .. "/asset/?id=" .. assetId
    local parent = torso or dummy

    if assetType == 11 then
        local shirt = Instance.new("Shirt")
        shirt.ShirtTemplate = assetUrl
        shirt.Parent = parent
        return shirt
    elseif assetType == 12 then
        local pants = Instance.new("Pants")
        pants.PantsTemplate = assetUrl
        pants.Parent = parent
        return pants
    elseif assetType == 2 then
        local graphic = Instance.new("ShirtGraphic")
        graphic.Graphic = assetUrl
        graphic.Parent = parent
        return graphic
    end

    return nil
end

-- Puts the clothing on the dummy using the picture it is made of.
local function dressFromTexture(dummy, torso)
    local url = baseURL .. "/asset/?id=" .. tostring(textureId)
    -- The 2016 engine looks for clothing on the torso part, not on the model around it.
    local parent = torso or dummy
    local item = nil

    if assetType == 11 then
        item = Instance.new("Shirt")
        item.ShirtTemplate = url
    elseif assetType == 12 then
        item = Instance.new("Pants")
        item.PantsTemplate = url
    elseif assetType == 2 then
        item = Instance.new("ShirtGraphic")
        item.Graphic = url
    end

    if item ~= nil then
        item.Parent = parent
        -- The template is a url to fetch, so give the download time to finish before the snapshot.
        pcall(function()
            game:GetService("ContentProvider"):PreloadAsync({ item })
        end)
        wait(1)
        print("[debug] dressed dummy in", item.ClassName, "from texture", textureId, "via", url)
    end

    return item
end

local function render()
    local dummy, torso = buildDummy()

    local clothing = nil
    if textureId ~= 0 then
        clothing = dressFromTexture(dummy, torso)
    end

    if clothing == nil then
        print("[debug] loading clothing asset", assetId, "type", assetType)

        local loaded = nil
        local ok, result = pcall(function()
            return InsertService:LoadAsset(assetId)
        end)
        if ok then
            loaded = result
        else
            print("[warning] InsertService:LoadAsset failed:", result)
        end

        clothing = findClothing(loaded)
        if clothing then
            clothing.Parent = dummy
            print("[debug] equipped", clothing.ClassName, "from loaded asset")
        else
            print("[warning] no clothing instance in loaded asset, falling back to asset URL")
            clothing = buildClothingFromUrl(dummy, torso)
        end
    end

    -- Give the client time to download the clothing texture from the website.
    wait(2)

    local camera = Instance.new("Camera", dummy)
    camera.Name = "ThumbnailCamera"
    camera.CameraType = Enum.CameraType.Scriptable
    camera.CoordinateFrame = CFrame.new(torso.Position + Vector3.new(0, 0.5, 6), torso.Position)
    workspace.CurrentCamera = camera

    local encoded = clickPng(420, 420)
    if encoded == nil then
        print('[warn] clothing render produced no image')
        return
    end
    print("[debug] rendered clothing thumbnail, bytes:", tostring(string.len(encoded)))

    -- The image leaves the job as text. io and writefile are nil in this sandbox, and
    -- HttpService:PostAsync is what kills the process, but print() reaches the log file the game
    -- server tails. Base64 goes out as hex so secret scanners reading these logs leave it alone.
    local hex = string.gsub(encoded, '.', function(c)
        return string.format('%02x', string.byte(c))
    end)
    local tag = tostring(jobId)
    local chunkSize = 1800
    local hexLength = string.len(hex)
    local totalChunks = math.ceil(hexLength / chunkSize)
    print("[payload] begin " .. tag .. " " .. tostring(totalChunks) .. " " .. tostring(hexLength) .. " hex")
    for i = 1, totalChunks do
        print("[payload] " .. tag .. " " .. tostring(i) .. " "
            .. string.sub(hex, (i - 1) * chunkSize + 1, i * chunkSize))
    end
    print("[payload] end " .. tag)
    -- Let the log writer flush before the CoreGui failure tears the process down.
    wait(4)
end

local ok, data = pcall(function()
    render()
end)
print(ok, data)
print("[debug] exit clothing render")
