local assetId = {1234};
local jobId = "InsertJobIdHere";
local baseURL = "http://127.0.0.1:5000";
local uploadURL = "UPLOAD_URL_HERE";
local HttpService = game:GetService("HttpService");
local ThumbnailGenerator = game:GetService('ThumbnailGenerator');
local InsertService = game:GetService("InsertService");

HttpService.HttpEnabled = true
game:GetService('StarterGui'):SetCoreGuiEnabled(Enum.CoreGuiType.All, false)
ThumbnailGenerator.GraphicsMode = 2

local function render()
    local numericAssetId = assetId[1]
    
    -- 1. Build a 3D Mannequin
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
    
    dummy.PrimaryPart = torso
    dummy.Parent = workspace

    -- 2. Load the asset safely
    local ok, asset = pcall(function()
        return InsertService:LoadAsset(numericAssetId)
    end)
    
    if ok and asset then
        -- If it's a Shirt, Pants, or T-Shirt, equip it to the dummy
        local clothing = asset:FindFirstChildOfClass("Shirt") or asset:FindFirstChildOfClass("Pants") or asset:FindFirstChildOfClass("ShirtGraphic")
        if not clothing then
            -- Check inside the model if it loaded as a nested object
            for _, child in pairs(asset:GetChildren()) do
                if child:IsA("Shirt") or child:IsA("Pants") or child:IsA("ShirtGraphic") then
                    clothing = child
                    break
                end
            end
        end
        
        if clothing then
            clothing.Parent = dummy
            print("[debug] Successfully equipped clothing to dummy!")
        else
            print("[debug] Could not find clothing object in asset.")
        end
    end
    
    wait(2) -- Wait for the texture to load from the website
    
    -- 3. Setup Camera
    local camera = Instance.new("Camera", dummy)
    camera.CameraType = Enum.CameraType.Scriptable
    camera.CoordinateFrame = CFrame.new(torso.Position + Vector3.new(0, 0, 6), torso.Position)
    workspace.CurrentCamera = camera
    
    -- 4. Click the picture
    ThumbnailGenerator:Click('png', 1, 1, true, false) -- Render once to fix mesh loading bug
    local encoded = ThumbnailGenerator:Click('png', 420, 420, true, true)
    
    local postOk, data = pcall(function()
        return HttpService:PostAsync(uploadURL, HttpService:JSONEncode({
            ['type'] = 'Asset',
            ['assetId'] = numericAssetId,
            ['thumbnail'] = encoded,
            ['accessKey'] = "AccessKey",
            ['jobId'] = jobId,
        }), Enum.HttpContentType.TextPlain)
    end)
    print("[debug] post over", postOk, data)
end

local ok, data = pcall(function()
    return render()
end)
print(ok, data)
print("[debug] exit asset render game")
