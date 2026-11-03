local jobId = "InsertJobIdHere";
local userId = 65789275746246;
local mode = "R6";
local baseURL = "http://economy-simulator.org";
local uploadURL = "UPLOAD_URL_HERE";
local ScriptContext = game:GetService("ScriptContext");
local Lighting = game:GetService('Lighting');
local RunService = game:GetService('RunService');
local ContentProvider = game:GetService('ContentProvider');
local HttpService = game:GetService("HttpService");
local ThumbnailGenerator = game:GetService('ThumbnailGenerator');
local Players = game:GetService("Players");
game:GetService('StarterGui'):SetCoreGuiEnabled(Enum.CoreGuiType.All, false);
-- 2 asks RCC for a graphics backend this build does not have, and the click crashes the
-- process. 4 is what the headshot script uses (and Roblox treats it as "let RCC decide").
-- 3 is the software OpenGL backend; OSMESA32.dll ships with this RCC build.
-- 4 means "no graphics context", and ThumbnailGenerator:Click then dies with
-- StartProcessException right after "thumbnail camera ready".
local _thumbGen = game:GetService('ThumbnailGenerator');
_thumbGen.GraphicsMode = 3;
print('[debug] graphics mode', _thumbGen.GraphicsMode);
HttpService.HttpEnabled = true;
ScriptContext.ScriptsDisabled = true
Lighting.Outlines = false
ContentProvider:SetBaseUrl('http://economy-simulator.org')
game:GetService("InsertService"):SetAssetUrl(baseURL .. "/asset/?id=%d")
game:GetService("InsertService"):SetAssetVersionUrl(baseURL .. "/Asset/?assetversionid=%d")
local Insert = game:GetService("InsertService")

local function applyMesh(Player, children, limb)
    local ok, msg = pcall(function() 
        local specialMesh = children[1]
        local head = Player.Character[limb]
        local m = head:FindFirstChild("Mesh")
        if not m then
            m = Instance.New("SpecialMesh")
            m.Parent = head
        end
        -- set
        m.Scale = specialMesh.Scale
        m.TextureId = specialMesh.TextureId
        m.MeshId = specialMesh.MeshId
        m.MeshType = specialMesh.MeshType
        m.VertexColor = specialMesh.VertexColor
    end)
    if not ok then
        print("error loading mesh", msg)
    end
end

local function applyPackage(Player, children)
    local ok, msg = pcall(function() 
        print("applyPackage children", children, #children)
        for _, asset in pairs(children) do
            print("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!Child package",asset)
            asset.Parent = Player.Character
        end
    end)
    if not ok then
        print("error loading package", msg)
    end
end

    local function render(id)
        -- game.StarterPlayer.GameSettingsAvatarType = Enum.GameAvatarType.R15
        local Player = Players:CreateLocalPlayer(id)
        -- Player.CharacterAppearance = "http://localhost/Asset/AvatarAccoutrements.ashx?userId=1127502";
        Player:LoadCharacter()

        local av = HttpService:JSONDecode('JSON_AVATAR')

        --[[
        if av.playerAvatarType == 'R15' then
            local Character = Insert:LoadAsset(1664543044).Player
            Character.Parent = game.Workspace
            print('char len', #Character:GetChildren())
            local Humanoid = Character.Humanoid
            Player.Character = Character
            local hrp = Player.Character.HumanoidRootPart
            hrp.Anchored = true
        end
        ]]--


        -- this returns data in an identical format to https://avatar.roblox.com/v1/users/1/avatar
        -- Player.Character['Right Leg']:Destroy()
        local done = 0
        for _, asset in pairs(av.assets) do
            print('[debug] add',asset.name,'to char')
            coroutine.wrap(function()
                local ok, Asset = pcall(function()
                    return Insert:LoadAsset(asset.id)
                end)
                if ok == false then
                    done = done + 1;
                    print("[debug] asset load fail for",asset.id,Asset)
                    return
                end
                local children = Asset:GetChildren()

                if asset.assetType.id == 17 then
                    applyMesh(Player, children, "Head")
                end
                if asset.assetType.id == 27 or asset.assetType.id == 28 or asset.assetType.id == 29 or asset.assetType.id == 30 or asset.assetType.id == 31 then
                    applyPackage(Player, children)
                else
                    for _, item in pairs(children) do
                        print('[debug] add ',asset.Name, '/', item.Name,'to char (type =',asset.assetType.id,')')
                        if asset.assetType.id == 31 then
                            print('[debug] Got right leg cl is',item.ClassName)
                        end
                        if asset.assetType.id == 18 then
                            local head = Player.Character.Head
                            if head:FindFirstChild("face") ~= nil then
                                head.face:Destroy()
                            end
                            item.Name = "face"
                            item.Parent = head
                        else
                            item.Parent = Player.Character
                        end
                    end
                end

                done = done + 1;
            end)()
        end
        repeat wait() until done == #av.assets
        print("[debug] set body colors")
        local bc = av.bodyColors;
        local colors = {
            ['Head']      = bc.headColorId,
            ['Torso']     = bc.torsoColorId,
            ['Left Arm']  = bc.leftArmColorId,
            ['Right Arm'] = bc.rightArmColorId,
            ['Left Leg']  = bc.leftLegColorId,
            ['Right Leg'] = bc.rightLegColorId
        }

        for part, color in pairs(colors) do
            if Player.Character:FindFirstChild(part) then
                Player.Character[part].BrickColor = BrickColor.new(color)
            else
                print("[warning] could not find",part,"in player")
            end
        end

        for _, object in pairs(Player.Character:GetChildren()) do
            if object:IsA('Tool') then
                print("Player has gear, raise the right arm out.")
                Player.Character.Torso['Right Shoulder'].CurrentAngle = math.rad(90)
            end
        end

        --[[
        local guy = Player.Character
        guy.Head.Mesh:remove()
        guy.Torso:remove()
        guy['Right Arm']:remove()
        guy['Left Arm']:remove()
        guy['Right Leg']:remove()
        guy['Left Leg']:remove()
        ]]--
        
        -- local humanoid = Player.Character.Humanoid
        -- humanoid:BuildRigFromAttachments()
        print("[debug] render avatar")
        -- The full body click needs its own scriptable camera. Checking for one and carrying on
        -- regardless (the old behaviour) leaves the place with no camera, and RCC's
        -- ThumbnailGenerator:Click then takes the whole RCC process down with it.
        local character = Player.Character
        local head = character and character:FindFirstChild("Head")
        if head ~= nil then
            local camera = workspace.CurrentCamera
            if camera == nil then
                camera = Instance.new("Camera")
                camera.Parent = character
            end
            camera.Name = "ThumbnailCamera"
            camera.CameraType = Enum.CameraType.Scriptable
            camera.CoordinateFrame = CFrame.new((head.CFrame * CFrame.new(0, 1.2, 11)).p, head.Position)
            camera.FieldOfView = 30
            workspace.CurrentCamera = camera
            print("[debug] thumbnail camera ready")
        else
            print("[warn] no head on the character, rendering with whatever camera the place has")
        end
        local avatarEncoded = ThumbnailGenerator:Click('png', _X_RES_, _Y_RES_, true)
        print("[debug] avatar render over. delete character")
        Player.Character:Destroy()
        print("[debug] [player/thumbnail] send post request containing avatar")
        local payload = HttpService:JSONEncode({
            ['thumbnail'] = avatarEncoded,
            ['userId'] = userId,
            ['accessKey'] = "AccessKey",
            ['type'] = "PlayerThumbnail",
            ['jobId'] = jobId,
        })
        -- Park the finished image on the job as well. Some RCC builds refuse every
        -- outbound HTTP request (TrustCheck), which leaves the render stuck forever at
        -- the post below. The game server reads this value back over SOAP instead.
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
        print("[debug] post over",ok,data)
    end

coroutine.wrap(function()
    local ok, data = pcall(function()
        render(1)
    end)
    print("[player/thumbnail]", ok, data);
    print("[debug] exit game");
end)()
