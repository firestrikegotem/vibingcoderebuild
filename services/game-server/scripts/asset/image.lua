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
-- RCC's TrustCheck refuses to load anything from "localhost", so always use the raw loopback IP.
local baseURL = "http://127.0.0.1:5000";
local uploadURL = "UPLOAD_URL_HERE";

local HttpService = game:GetService("HttpService");
local ThumbnailGenerator = game:GetService('ThumbnailGenerator');
local InsertService = game:GetService("InsertService");
local ContentProvider = game:GetService("ContentProvider");

HttpService.HttpEnabled = true
game:GetService('StarterGui'):SetCoreGuiEnabled(Enum.CoreGuiType.All, false)
ThumbnailGenerator.GraphicsMode = 2

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
local function buildClothingFromUrl(dummy)
    local assetUrl = baseURL .. "/asset/?id=" .. assetId

    if assetType == 11 then
        local shirt = Instance.new("Shirt")
        shirt.ShirtTemplate = assetUrl
        shirt.Parent = dummy
        return shirt
    elseif assetType == 12 then
        local pants = Instance.new("Pants")
        pants.PantsTemplate = assetUrl
        pants.Parent = dummy
        return pants
    elseif assetType == 2 then
        local graphic = Instance.new("ShirtGraphic")
        graphic.Graphic = assetUrl
        graphic.Parent = dummy
        return graphic
    end

    return nil
end

local function render()
    local dummy, torso = buildDummy()

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

    local clothing = findClothing(loaded)
    if clothing then
        clothing.Parent = dummy
        print("[debug] equipped", clothing.ClassName, "from loaded asset")
    else
        print("[warning] no clothing instance in loaded asset, falling back to asset URL")
        clothing = buildClothingFromUrl(dummy)
    end

    -- Give the client time to download the clothing texture from the website.
    wait(2)

    local camera = Instance.new("Camera", dummy)
    camera.Name = "ThumbnailCamera"
    camera.CameraType = Enum.CameraType.Scriptable
    camera.CoordinateFrame = CFrame.new(torso.Position + Vector3.new(0, 0.5, 6), torso.Position)
    workspace.CurrentCamera = camera

    -- First click warms up mesh/texture loading (known RCC quirk), the second is the real render.
    ThumbnailGenerator:Click('png', 1, 1, true)
    local encoded = ThumbnailGenerator:Click('png', 420, 420, true)
    print("[debug] rendered clothing thumbnail, bytes:", tostring(#tostring(encoded)))

    local payload = HttpService:JSONEncode({
            ['thumbnail'] = encoded,
            ['assetId'] = assetId,
            ['accessKey'] = "AccessKey",
            ['type'] = "Image",
            ['jobId'] = jobId,
        })
 -- Park the finished image on the job as well. Some RCC builds refuse every
 -- outbound HTTP request (TrustCheck), which leaves the render stuck forever.
 -- The game server reads this value back over SOAP instead.
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
    local postOk, data = pcall(function()
        return HttpService:PostAsync(uploadURL, payload, Enum.HttpContentType.TextPlain)
    end)
    print("[debug] post over", postOk, data)
end

local ok, data = pcall(function()
    render()
end)
print(ok, data)
print("[debug] exit clothing render")
