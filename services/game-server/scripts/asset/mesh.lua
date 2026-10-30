local assetId = {1234};
local jobId = "InsertJobIdHere";
local baseURL = "http://127.0.0.1:5000";
local uploadURL = "UPLOAD_URL_HERE";
local ScriptContext = game:GetService("ScriptContext");
local Lighting = game:GetService('Lighting');
local ContentProvider = game:GetService('ContentProvider');
local HttpService = game:GetService("HttpService");
local ThumbnailGenerator = game:GetService('ThumbnailGenerator');

game:GetService('StarterGui'):SetCoreGuiEnabled(Enum.CoreGuiType.All, false);
ThumbnailGenerator.GraphicsMode = 2;
HttpService.HttpEnabled = true;
ScriptContext.ScriptsDisabled = true
Lighting.Outlines = false
ContentProvider:SetBaseUrl(baseURL)

local function concat(one, two, three)
    return one .. two .. three
end

local function render()
    local MeshPartContainer = Instance.new("Part");
    local numericAssetId = assetId[1];
    local renderMeshExample = Instance.new("FileMesh", MeshPartContainer);
    renderMeshExample.MeshId = concat(baseURL, "/asset/?id=", tostring(numericAssetId));

    local charModel = Instance.new("Model", game.Workspace);
    MeshPartContainer.Parent = charModel;

    print("[debug] render test asset")
    wait(4)
    
    -- Setup Camera
    local camera = Instance.new("Camera", charModel)
    camera.CameraType = Enum.CameraType.Scriptable
    camera.CoordinateFrame = CFrame.new(MeshPartContainer.Position + Vector3.new(0, 0, 5), MeshPartContainer.Position)
    workspace.CurrentCamera = camera
    
    print("load now")
    local encoded = ThumbnailGenerator:Click('png', _X_RES_, _Y_RES_, true, false)
    print("[debug] send post request containing test asset")

    local ok, data = pcall(function()
        return HttpService:PostAsync(uploadURL, HttpService:JSONEncode({
            ['type'] = 'Asset',
            ['assetId'] = numericAssetId,
            ['thumbnail'] = encoded,
            ['accessKey'] = "AccessKey",
            ['jobId'] = jobId,
        }), Enum.HttpContentType.TextPlain)
    end)
    print("[debug] post over",ok,data)
end

local ok, data = pcall(function()
    render()
end)
print(ok, data);
print("[debug] exit game");
