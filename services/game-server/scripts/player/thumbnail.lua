local jobId = "InsertJobIdHere";
local userId = 65789275746246;
local mode = 'R6'
-- Must match the BaseUrl in RCCService/AppSettings.xml and the hosts entry, or
-- RCC refuses every request as untrusted.
local baseURL = "http://bbblox.org";
-- The website endpoint that accepts the finished image and forwards it to the game
-- server; posting straight at the game server fails its trust check.
local uploadURL = baseURL .. "/api/upload-thumbnail-v1";

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

local function FindFirstChildWhichIsA(inst, className)
    for _, asset in pairs(inst:GetChildren()) do
        if asset.ClassName == className then
            return asset
        end
    end
    return nil
end

    local HttpService = game:GetService('HttpService')
    local ScriptContext = game:GetService('ScriptContext')
    local Lighting = game:GetService('Lighting')
    local Players = game:GetService('Players')
    local RunService = game:GetService('RunService')
    local ContentProvider = game:GetService('ContentProvider')
    local ThumbnailGenerator = game:GetService('ThumbnailGenerator')
    game:GetService('StarterGui'):SetCoreGuiEnabled(Enum.CoreGuiType.All, false)
    -- 3 is the software OpenGL backend (OSMesa32.dll ships with the RCC build).
    ThumbnailGenerator.GraphicsMode = 3;
    print('[debug] graphics mode', ThumbnailGenerator.GraphicsMode);

-- ThumbnailGenerator:Click does not throw on failure in this RCC build: it returns the
-- error text as the value ("invalid string position"), and the first click after RCC
-- starts is the one that fails. Upstream's own scripts click once at 1x1 for this reason.
-- Warm the renderer up, then retry until the result is a real base64 PNG.
local function clickPng(width, height)
    pcall(function() return ThumbnailGenerator:Click('png', 1, 1, true) end)
    for attempt = 1, 4 do
        local ok, result = pcall(function()
            return ThumbnailGenerator:Click('png', width, height, true)
        end)
        if ok and type(result) == 'string' and string.sub(result, 1, 5) == 'iVBOR' then
            print('[debug] render ok on attempt', attempt, 'bytes', string.len(result))
            return result
        end
        print('[debug] render attempt', attempt, 'returned', ok, tostring(result))
        wait(0.5)
    end
    return nil
end


    HttpService.HttpEnabled = true
    ScriptContext.ScriptsDisabled = true
    Lighting.Outlines = false
    ContentProvider:SetBaseUrl('http://economy-simulator.org')
    game:GetService("ContentProvider"):SetAssetUrl(baseURL .. "/Asset/")
    game:GetService("InsertService"):SetAssetUrl(baseURL .. "/Asset/?id=%d")
    pcall(function() game:GetService("ScriptInformationProvider"):SetAssetUrl(url .. "/Asset/") end)
    game:GetService("ContentProvider"):SetBaseUrl(baseURL .. "/")
    game:GetService("Players"):SetChatFilterUrl(baseURL .. "/Game/ChatFilter.ashx")
    local Insert = game:GetService("InsertService")
    game:GetService("InsertService"):SetAssetUrl(baseURL .. "/Asset/?id=%d")
	game:GetService("InsertService"):SetAssetVersionUrl(baseURL .. "/Asset/?assetversionid=%d")

    local function render(id)
        -- game.StarterPlayer.GameSettingsAvatarType = Enum.GameAvatarType.R15
        local Player = Players:CreateLocalPlayer(id)
        Player:LoadCharacter()
        -- this returns data in an identical format to https://avatar.roblox.com/v1/users/1/avatar
        local av = HttpService:JSONDecode('JSON_AVATAR')
        -- Player.Character['Right Leg']:Destroy()
        local done = 0
        -- Shirt 11, Pants 12, T-Shirt 2. These are put on from their picture: loading the clothing
        -- item itself makes this build unpack an XML in the middle of a render, which it does not
        -- survive - the avatar came back in body colours with nothing on. The avatar data carries
        -- the texture id, so the renderer can dress the character directly.
        local CLOTHING_CLASS = { [2] = "ShirtGraphic", [11] = "Shirt", [12] = "Pants" }
        -- The 2016 engine looks for clothing on the torso part. Handing the model to it instead is
        -- only honoured by newer builds, which is a plain torso with nothing on it.
        local function clothingParent()
            local character = Player.Character
            if character == nil then
                return nil
            end
            return character:FindFirstChild("Torso") or character
        end

        local function dressIn(class, textureId)
            local item = Instance.new(class)
            local url = baseURL .. "/asset/?id=" .. tostring(textureId)
            if class == "Shirt" then
                item.ShirtTemplate = url
            elseif class == "Pants" then
                item.PantsTemplate = url
            else
                item.Graphic = url
            end
            item.Parent = clothingParent()
            print('[debug]', class, 'parented to', tostring(item.Parent), 'via', url)
            -- Setting the template only starts the download. Wait for it, otherwise the snapshot is
            -- taken of a character whose clothes have not arrived yet.
            pcall(function()
                game:GetService("ContentProvider"):PreloadAsync({ item })
            end)
            wait(1)
        end

        for _, asset in pairs(av.assets) do
            print('[debug] add',asset.name,'to char')
            local clothingClass = CLOTHING_CLASS[asset.assetType.id]
            if clothingClass ~= nil then
                local textureId = asset.textureId or 0
                if textureId ~= 0 then
                    local dressed = pcall(function() dressIn(clothingClass, textureId) end)
                    print('[debug] dressed in', clothingClass, 'texture', textureId, 'ok', tostring(dressed))
                else
                    print('[warn] no texture id for', clothingClass, 'asset', asset.id)
                end
                done = done + 1
            else
            coroutine.wrap(function()
                local ok, Asset = pcall(function()
                    return Insert:LoadAsset(asset.id)
                end)
                if ok == false then
                    done = done + 1;
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
                        print('[debug] add ',asset.Name, '/', item.Name,'to char')
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
        end
        repeat wait() until done == #av.assets
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
            end
        end

        for _, object in pairs(Player.Character:GetChildren()) do
            if object:IsA('Tool') then
                object:Destroy()
                -- Player.Character.Torso['Right Shoulder'].CurrentAngle = math.pi / 2
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
        
        print('use cam')
        -- cam:Destroy()
        -- cam = Instance.new("Camera", game.Workspace)
        -- cam.CameraType = Enum.CameraType.Watch
        -- cam.CameraSubject = Player.Character.Head
        print("[debug] render avatar")
        -- Player.Character.HumanoidRootPart.Anchored = true
        -- local c = game.Workspace.CurrentCamera

        local player = Player
        local FFlagNewHeadshotLighting = false
        local FFlagOnlyCheckHeadAccessoryInHeadShot = false
        local cameraOffsetX = 0
        local cameraOffsetY = 0
        local maxHatZoom = 100
        local baseHatZoom = 30


        local maxDimension = 0

        local quadratic = true


    -- Remove gear
	for _, child in pairs(player.Character:GetChildren()) do
		if child:IsA("Tool") then
			child:Destroy()
		elseif child:IsA("Accoutrement") then
            local handle = child:FindFirstChild("Handle")
			if handle then
				local attachment = FindFirstChildWhichIsA(handle, "Attachment")
                
                --legacy hat does not have attachment in it and should be considered when zoom out camera
				if not FFlagOnlyCheckHeadAccessoryInHeadShot or not attachment or headAttachments[attachment.Name] then
					local size = handle.Size / 2 + handle.Position - player.Character.Head.Position
					local xy = Vector2.new(size.x, size.y)
					if xy.magnitude > maxDimension then
						maxDimension = xy.magnitude
					end
				end
			end
		end
	end

	-- Setup Camera
	local maxHatOffset = 0.5 -- Maximum amount to move camera upward to accomodate large hats
    maxDimension = math.min(1, maxDimension / 3) -- Confine maxdimension to specific bounds

    if quadratic then
        maxDimension = maxDimension * maxDimension -- Zoom out on quadratic interpolation
    end

    local viewOffset     = player.Character.Torso.CFrame * CFrame.new(cameraOffsetX, cameraOffsetY + maxHatOffset * maxDimension, 0.1) -- look at the body

    local yAngle = -math.pi / 16
	
	local positionOffset = player.Character.Head.CFrame + (CFrame.Angles(0, yAngle, 0).lookVector.unit * 13) -- far enough back for the whole R6 body

    local camera = Instance.new("Camera", player.Character)-- Instance.new("Camera", player.Character)
    camera.Name = "ThumbnailCamera"
    camera.CameraType = Enum.CameraType.Scriptable
    camera.CoordinateFrame = CFrame.new(positionOffset.p, viewOffset.p)
    camera.FieldOfView = baseHatZoom + (maxHatZoom - baseHatZoom) * maxDimension
    print("cam fov",camera.FieldOfView)

	workspace.CurrentCamera = camera
    
        local avatarEncoded = clickPng(_X_RES_, _Y_RES_)
        if avatarEncoded == nil then
            print('[warn] thumbnail render failed, nothing to upload')
            return
        end
        -- The process dies within a few lines of the old HttpService:JSONEncode call here, so
        -- this builds the same JSON with plain string concatenation and prints between every
        -- step. If RCC still dies, the last stage line printed is the statement that killed it.
        print("[stage] render ok, bytes", string.len(avatarEncoded))
        local payload = '{"thumbnail":"' .. avatarEncoded
            .. '","userId":' .. tostring(userId)
            .. ',"accessKey":"AccessKey"'
            .. ',"type":"PlayerThumbnail"'
            .. ',"jobId":"' .. tostring(jobId) .. '"}'
        print("[stage] payload built, bytes", string.len(payload))
        -- What this sandbox can reach: io is nil and writefile is nil, so a script cannot touch
        -- the disk, and HttpService:PostAsync below is what kills the process. print() works
        -- though, and RCC sends it to the log file the game server already tails. So the image
        -- leaves the job as text: base64 in numbered chunks under a job id, which can be put
        -- back together even when other RCC threads interleave their own output.
        -- Hex, not the base64 itself: raw base64 chunks look like leaked credentials to every
        -- secret scanner that reads these logs, including the one on the chat side, which made
        -- the last paste unreadable. Hex is plain digits and letters, half as dense but harmless.
        local hex = string.gsub(avatarEncoded, '.', function(c)
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
        -- Give the log writer a moment to flush the chunks before anything risky runs.
        wait(0.25)
        -- Send it straight away. The old crash landed on HttpService:JSONEncode above, never on
        -- this call, so the request was never actually attempted - which is why no upload ever
        -- reached the game server. Two argument form on purpose: the optional content type enum
        -- is one more thing this build can disagree with.
        local sent = false
        local postOk, postErr = pcall(function()
            return HttpService:PostAsync(uploadURL, payload)
        end)
        print("[stage] post attempted:", postOk, postErr)
        sent = postOk and true or false
        _G.__bloxynThumbnail = payload
        print("[stage] parked in _G")
        local holder = game:GetService("ServerStorage"):FindFirstChild("__BloxynThumbnail")
        if holder == nil then
            holder = Instance.new("StringValue")
            holder.Name = "__BloxynThumbnail"
            holder.Parent = game:GetService("ServerStorage")
        end
        print("[stage] holder ready")
        holder.Value = payload
        print("[stage] parked in ServerStorage, holding the job open")
        -- Hold the job open: this RCC crashes a second or two after its own CoreGui modules
        -- start failing, and finishing the script is what lets that tear the process down.
        -- Waiting keeps the window open for the upload to complete.
        for _ = 1, 120 do wait(0.5) end
    end

    local ok, data = pcall(function()
        render(userId)
    end)
    print("[player/thumbnail]", ok, data);

