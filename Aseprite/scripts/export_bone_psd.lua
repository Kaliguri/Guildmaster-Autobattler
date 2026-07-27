-- Export Bone PSD
-- Builds a Photoshop-ready layered .psd for the Unity 2D Animation PSB pipeline:
--   same layer conventions as export_bone_parts.lua, FULL CANVAS (no trim),
--   optional nearest upscale, then PSD via vendored Tsukina exporter.
--
-- After export: open .psd in Photoshop → arrange if needed → Save As .psb
-- (Maximize Compatibility) → import into Unity (Character Rig).
--
-- Layer conventions:
--   image layer          -> kept as-is
--   group (no prefix)    -> kept (children intact; Use Layer Grouping in Unity)
--   group "@Name"        -> flatten visible children into one layer "Name"
--   name starts with #/_ -> removed (guides / refs)
--   groups Arm / Leg     -> duplicated as "Arm (left)" / "Arm (right)" (same for Leg)
--                          (identical copies; arrange/mirror later in Photoshop)

local DEFAULT_SCALE = 10

-- Top-level-ish limb group names (case-insensitive exact match).
local LIMB_GROUP_NAMES = {
  arm = true,
  leg = true,
}

---------------------------------------------------------------------------
-- Paths / vendor
---------------------------------------------------------------------------

local function scriptDir()
  local info = debug.getinfo(1, "S")
  local src = info and info.source or ""
  if src:sub(1, 1) == "@" then
    src = src:sub(2)
  end
  return app.fs.filePath(src)
end

local function loadPsdExporter()
  if type(ExportToPsd) == "function" then
    return true
  end
  local path = app.fs.joinPath(scriptDir(), "vendor", "export_as_psd.lua")
  if not app.fs.isFile(path) then
    return false, "Missing vendor PSD exporter:\n" .. path
  end
  _G.GUILDMASTER_PSD_LIB_ONLY = true
  local ok, err = pcall(function()
    dofile(path)
  end)
  _G.GUILDMASTER_PSD_LIB_ONLY = nil
  if not ok then
    return false, tostring(err)
  end
  if type(ExportToPsd) ~= "function" then
    return false, "ExportToPsd was not registered by vendor/export_as_psd.lua"
  end
  return true
end

---------------------------------------------------------------------------
-- Layer helpers (shared conventions)
---------------------------------------------------------------------------

local function startsWith(str, prefix)
  return str:sub(1, #prefix) == prefix
end

local function shouldSkip(name)
  return startsWith(name, "#") or startsWith(name, "_")
end

local function cleanExportName(name)
  if startsWith(name, "@") then
    return name:sub(2)
  end
  return name
end

local function renderCelOnto(canvas, layer, frame)
  local cel = layer:cel(frame)
  if not cel or not cel.image then
    return false
  end
  local opacity = 255
  if layer.opacity ~= nil then
    opacity = layer.opacity
  end
  local blend = BlendMode.NORMAL
  if layer.blendMode ~= nil then
    blend = layer.blendMode
  end
  canvas:drawImage(cel.image, cel.position, opacity, blend)
  return true
end

local function flattenGroupToImage(sprite, group, frame)
  local canvas = Image(sprite.spec)
  canvas:clear()

  local function drawLayers(layers)
    for i = 1, #layers do
      local layer = layers[i]
      if shouldSkip(layer.name) then
        -- skip
      elseif not layer.isVisible then
        -- @-merge: only visible children
      elseif layer.isGroup then
        drawLayers(layer.layers)
      elseif layer.isImage then
        renderCelOnto(canvas, layer, frame)
      end
    end
  end

  drawLayers(group.layers)
  return canvas
end

--- Nearest-neighbor upscale for the whole sprite (pixel-art safe).
local function scaleSpriteNearest(sprite, scale)
  if scale == 1 then
    return
  end
  app.activeSprite = sprite
  app.command.SpriteSize{
    ui = false,
    width = sprite.width * scale,
    height = sprite.height * scale,
    method = "nearest",
  }
end

---------------------------------------------------------------------------
-- Prepare clone: drop guides, flatten @groups
---------------------------------------------------------------------------

local function collectAtGroups(layers, out)
  for i = 1, #layers do
    local layer = layers[i]
    if layer.isGroup then
      if startsWith(layer.name, "@") and not shouldSkip(layer.name) then
        table.insert(out, layer)
      else
        collectAtGroups(layer.layers, out)
      end
    end
  end
end

local function collectSkipLayers(layers, out)
  for i = 1, #layers do
    local layer = layers[i]
    if shouldSkip(layer.name) then
      table.insert(out, layer)
    elseif layer.isGroup then
      collectSkipLayers(layer.layers, out)
    end
  end
end

local function isLimbGroup(layer)
  if not layer.isGroup then
    return false
  end
  return LIMB_GROUP_NAMES[layer.name:lower()] == true
end

local function collectLimbGroups(layers, out)
  for i = 1, #layers do
    local layer = layers[i]
    if isLimbGroup(layer) then
      table.insert(out, layer)
    elseif layer.isGroup then
      collectLimbGroups(layer.layers, out)
    end
  end
end

--- Duplicate Arm/Leg groups → "(left)" + "(right)" copies (same pixels; pose in PS).
local function duplicateLimbGroups(spr)
  app.activeSprite = spr
  local limbs = {}
  collectLimbGroups(spr.layers, limbs)
  for _, group in ipairs(limbs) do
    local base = group.name
    app.activeLayer = group
    app.command.DuplicateLayer()
    local dup = app.activeLayer
    group.name = base .. " (left)"
    if dup and dup ~= group then
      dup.name = base .. " (right)"
    end
  end
end

local function prepareClone(source, frame, scale)
  local spr = Sprite(source)

  -- Drop guides / refs
  local toDelete = {}
  collectSkipLayers(spr.layers, toDelete)
  for i = #toDelete, 1, -1 do
    spr:deleteLayer(toDelete[i])
  end

  -- Flatten @groups into single layers (process top-most first so indices stay sane)
  local atGroups = {}
  collectAtGroups(spr.layers, atGroups)
  for i = #atGroups, 1, -1 do
    local group = atGroups[i]
    local name = cleanExportName(group.name)
    local flat = flattenGroupToImage(spr, group, frame)
    local parent = group.parent
    local stackIndex = group.stackIndex

    local newLayer = spr:newLayer()
    newLayer.name = name
    newLayer.isVisible = group.isVisible
    if group.opacity ~= nil then
      newLayer.opacity = group.opacity
    end

    -- Place near the old group: stackIndex is 1-based from bottom in recent API
    if stackIndex ~= nil then
      newLayer.stackIndex = stackIndex
    end
    if parent and parent ~= spr and parent.isGroup then
      newLayer.parent = parent
    end

    spr:newCel(newLayer, frame, flat, Point(0, 0))
    spr:deleteLayer(group)
  end

  -- Arm/Leg → left + right group copies for Photoshop layout
  duplicateLimbGroups(spr)

  if scale > 1 then
    scaleSpriteNearest(spr, scale)
  end

  return spr
end

---------------------------------------------------------------------------
-- Entry
---------------------------------------------------------------------------

local source = app.activeSprite
if not source then
  return app.alert("No active sprite. Open a .aseprite file first.")
end

if not source.filename or source.filename == "" then
  return app.alert("Sprite is unsaved. Save the .aseprite first.")
end

local loaded, loadErr = loadPsdExporter()
if not loaded then
  return app.alert(loadErr)
end

local defaultOut = app.fs.filePathAndTitle(source.filename) .. ".psd"
local frame = app.activeFrame or source.frames[1]
local frameNumber = frame.frameNumber
local scale = DEFAULT_SCALE
local outPath = defaultOut
local doRun = true

if app.isUIAvailable then
  local dlg = Dialog{ title = "Export Bone PSD" }
  dlg:label{ id = "src", label = "Source", text = app.fs.fileName(source.filename) }
  dlg:file{
    id = "out",
    label = "PSD output",
    title = "Save PSD",
    save = true,
    filename = defaultOut,
    filetypes = { "psd" },
  }
  dlg:number{ id = "scale", label = "Scale (nearest)", text = tostring(DEFAULT_SCALE), decimals = 0 }
  dlg:label{
    id = "hint",
    label = "Next",
    text = "Open PSD in Photoshop → Save As .psb (Maximize Compatibility)",
  }
  dlg:separator()
  dlg:label{
    id = "rules",
    label = "Rules",
    text = "@Group = merge | Arm/Leg → (left)+(right) | #/_ = drop",
  }
  dlg:button{ id = "ok", text = "Export PSD" }
  dlg:button{ id = "cancel", text = "Cancel" }
  dlg:show()

  if not dlg.data.ok then
    doRun = false
  else
    outPath = dlg.data.out
    scale = math.floor(tonumber(dlg.data.scale) or DEFAULT_SCALE)
  end
else
  -- CLI: --script-param out=... --script-param scale=1
  if app.params["out"] or app.params["o"] or app.params["filename"] then
    outPath = app.params["out"] or app.params["o"] or app.params["filename"]
  end
  if app.params["scale"] then
    scale = math.floor(tonumber(app.params["scale"]) or DEFAULT_SCALE)
  end
end

if not doRun then
  return
end

if scale < 1 then
  scale = 1
end

local prepared = prepareClone(source, frame, scale)
app.activeSprite = prepared

local ok, message = ExportToPsd(prepared, outPath, frameNumber)

-- Close the temporary clone without a save prompt
app.activeSprite = prepared
app.command.CloseFile{ ui = false }

pcall(function()
  app.activeSprite = source
end)

if not ok then
  local msg = message or "unknown error"
  if app.isUIAvailable then
    app.alert{ title = "Export Bone PSD Failed", text = tostring(msg) }
  else
    print("Export Bone PSD failed: " .. tostring(msg))
  end
  return
end

local done = "PSD exported:\n" .. outPath ..
  "\n\nNext: Photoshop → Save As .psb (Maximize Compatibility)\n" ..
  "Then Unity: Multiple + Mosaic + Character Rig + Use Layer Grouping"
if app.isUIAvailable then
  app.alert{ title = "Export Bone PSD", text = done }
else
  print(done)
end
