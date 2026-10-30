local jobId = "InsertJobIdHere";
local assetId = 65789275746246;
local assetType = 358843;
local baseURL = "http://127.0.0.1:5000";
local uploadURL = "UPLOAD_URL_HERE";
local HttpService = game:GetService("HttpService");
local ThumbnailGenerator = game:GetService('ThumbnailGenerator');

HttpService.HttpEnabled = true
game:GetService('StarterGui'):SetCoreGuiEnabled(Enum.CoreGuiType.All, false)
ThumbnailGenerator.GraphicsMode = 4

local function render(id)
    local assetUrl = baseURL .. "/asset/?id=" .. assetId;
    
    local dummy = Instance.new("Model")
    local humanoid = Instance.new("Humanoid")
    humanoid.Parent = dummy
    
    local torso = Instance.new("Part")
    torso.Name = "Torso"
    torso.Size = Vector3.new(2, 2, 1)
    torso.Position = Vector3.new(0, 3, 0)
    torso.BrickColor = BrickColor.new("Institutional white")
    torso.Anchored = true
    torso.Parent = dummy
    
    local head = Instance.new("Part")
    head.Name = "Head"
    head.Size = Vector3.new(2, 1, 1)
    head.Position = Vector3.new(0, 4.5, 0)
    head.BrickColor = BrickColor.new("Institutional white")
    head.Anchored = true
    head.Parent = dummy
    
    dummy.PrimaryPart = torso
    dummy.Parent = workspace

    if assetType == 11 then
        local shirt = Instance.new("Shirt"); shirt.ShirtTemplate = assetUrl; shirt.Parent = dummy
    elseif assetType == 12 then
        local pants = Instance.new("Pants"); pants.PantsTemplate = assetUrl; pants.Parent = dummy
    end
    
    wait(2)
    
    local camera = Instance.new("Camera", dummy)
    camera.CameraType = Enum.CameraType.Scriptable
    camera.CoordinateFrame = CFrame.new(torso.Position + Vector3.new(0, 0, 6), torso.Position)
    workspace.CurrentCamera = camera
    
    local avatarEncoded = ThumbnailGenerator:Click('png', 420, 420, true, true)
    
    local ok, data = pcall(function()
        return HttpService:PostAsync(uploadURL, HttpService:JSONEncode({
            ['thumbnail'] = avatarEncoded, ['assetId'] = assetId, ['accessKey'] = "AccessKey", ['type'] = "Image", ['jobId'] = jobId,
        }), Enum.HttpContentType.TextPlain)
    end)
end

local ok, data = pcall(function() render(assetId) end)
print(ok, data)
