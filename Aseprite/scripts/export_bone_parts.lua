-- Export Bone Parts
-- Trims each exportable part to opaque bounds, nearest-neighbor scales,
-- and writes PNGs into:
--   Assets/_Project/Art/Sprites/Bone Animations/<aseprite-basename>/
--
-- Layer conventions:
--   image layer          -> one PNG named after the layer
--   group (no prefix)    -> recurse, export children
--   group "@Name"        -> flatten visible children into Name.png
--   name starts with #/_ -> skip (guides / refs)

local DEFAULT_SCALE = 10
local OUTPUT_REL = app.fs.joinPath("Assets", "_Project", "Art", "Sprites", "Bone Animations")

---------------------------------------------------------------------------
-- Helpers
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

local function findProjectRoot(startFile)
  local dir = app.fs.filePath(startFile)
  while dir and dir ~= "" do
    local marker = app.fs.joinPath(dir, "Assets", "_Project")
    if app.fs.isDirectory(marker) then
      return dir
    end
    local parent = app.fs.filePath(dir)
    if parent == dir or parent == nil or parent == "" then
      break
    end
    dir = parent
  end
  return nil
end

local function ensureDir(path)
  if app.fs.isDirectory(path) then
    return true
  end
  -- create parents
  local parent = app.fs.filePath(path)
  if parent and parent ~= path and not app.fs.isDirectory(parent) then
    if not ensureDir(parent) then
      return false
    end
  end
  return app.fs.makeDirectory(path)
end

--- Nearest-neighbor upscale (Image:resize only documents bilinear/rotsprite).
local function scaleNearest(src, scale)
  if scale == 1 then
    return src:clone()
  end
  local dst = Image(src.width * scale, src.height * scale, src.colorMode)
  dst:clear()
  for y = 0, src.height - 1 do
    for x = 0, src.width - 1 do
      local c = src:getPixel(x, y)
      local dx0 = x * scale
      local dy0 = y * scale
      for dy = 0, scale - 1 do
        for dx = 0, scale - 1 do
          dst:drawPixel(dx0 + dx, dy0 + dy, c)
        end
      end
    end
  end
  return dst
end

local function trimImage(img)
  local bounds = img:shrinkBounds()
  if bounds.width <= 0 or bounds.height <= 0 then
    return nil
  end
  return Image(img, bounds)
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

--- Flatten a group's image layers (bottom → top) onto a full-canvas image.
local function flattenGroup(sprite, group, frame)
  local canvas = Image(sprite.spec)
  canvas:clear()

  local function drawLayers(layers)
    for i = 1, #layers do
      local layer = layers[i]
      if shouldSkip(layer.name) then
        -- skip guides
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

local function exportImage(img, outPath, scale, sprite)
  local trimmed = trimImage(img)
  if not trimmed then
    return false, "empty"
  end
  local scaled = scaleNearest(trimmed, scale)
  -- Indexed images need a palette when saving alone
  if scaled.colorMode == ColorMode.INDEXED then
    scaled:saveAs{ filename = outPath, palette = sprite.palettes[1] }
  else
    scaled:saveAs(outPath)
  end
  return true, nil
end

---------------------------------------------------------------------------
-- Walk & export
---------------------------------------------------------------------------

local function collectAndExport(sprite, frame, outDir, scale, report)
  local function walk(layers)
    for i = 1, #layers do
      local layer = layers[i]
      local name = layer.name

      if shouldSkip(name) then
        report.skipped = report.skipped + 1
      elseif layer.isGroup and startsWith(name, "@") then
        local exportName = cleanExportName(name)
        local canvas = flattenGroup(sprite, layer, frame)
        local outPath = app.fs.joinPath(outDir, exportName .. ".png")
        local ok, err = exportImage(canvas, outPath, scale, sprite)
        if ok then
          report.exported = report.exported + 1
          table.insert(report.files, exportName .. ".png")
        else
          report.empty = report.empty + 1
          table.insert(report.warnings, exportName .. " (" .. tostring(err) .. ")")
        end
      elseif layer.isGroup then
        walk(layer.layers)
      elseif layer.isImage then
        local cel = layer:cel(frame)
        if not cel or not cel.image then
          report.empty = report.empty + 1
          table.insert(report.warnings, name .. " (no cel)")
        else
          -- Use cel image directly (already content-sized); trim again for safety
          local outPath = app.fs.joinPath(outDir, name .. ".png")
          local ok, err = exportImage(cel.image, outPath, scale, sprite)
          if ok then
            report.exported = report.exported + 1
            table.insert(report.files, name .. ".png")
          else
            report.empty = report.empty + 1
            table.insert(report.warnings, name .. " (" .. tostring(err) .. ")")
          end
        end
      end
    end
  end

  walk(sprite.layers)
end

---------------------------------------------------------------------------
-- Entry
---------------------------------------------------------------------------

local sprite = app.activeSprite
if not sprite then
  return app.alert("No active sprite. Open a .aseprite file first.")
end

if not sprite.filename or sprite.filename == "" then
  return app.alert("Sprite is unsaved. Save the .aseprite into the project Aseprite/ folder first.")
end

local projectRoot = findProjectRoot(sprite.filename)
if not projectRoot then
  return app.alert(
    "Cannot find project root (Assets/_Project).\n" ..
    "Save the .aseprite under the repo (e.g. Aseprite/Bone Animations/)."
  )
end

local basename = app.fs.fileTitle(sprite.filename)
local outDir = app.fs.joinPath(projectRoot, OUTPUT_REL, basename)
local defaultScale = DEFAULT_SCALE

local scale = defaultScale
local doRun = true

if app.isUIAvailable then
  local dlg = Dialog{ title = "Export Bone Parts" }
  dlg:label{ id = "src", label = "Source", text = app.fs.fileName(sprite.filename) }
  dlg:label{ id = "dst", label = "Output", text = outDir }
  dlg:number{ id = "scale", label = "Scale (nearest)", text = tostring(defaultScale), decimals = 0 }
  dlg:separator()
  dlg:label{
    id = "hint",
    label = "Rules",
    text = "@Group = merge | #/_ = skip | other groups = children"
  }
  dlg:button{ id = "ok", text = "Export" }
  dlg:button{ id = "cancel", text = "Cancel" }
  dlg:show()

  if not dlg.data.ok then
    doRun = false
  else
    scale = math.floor(tonumber(dlg.data.scale) or defaultScale)
  end
end

if not doRun then
  return
end

if scale < 1 then
  scale = 1
end

if not ensureDir(outDir) then
  return app.alert("Failed to create output folder:\n" .. outDir)
end

local frame = app.activeFrame
if not frame then
  frame = sprite.frames[1]
end

local report = {
  exported = 0,
  empty = 0,
  skipped = 0,
  files = {},
  warnings = {},
}

collectAndExport(sprite, frame, outDir, scale, report)

local msg = string.format(
  "Exported %d PNG(s) @ %dx\n→ %s",
  report.exported,
  scale,
  outDir
)
if #report.warnings > 0 then
  msg = msg .. "\n\nSkipped/empty:\n- " .. table.concat(report.warnings, "\n- ")
end

if app.isUIAvailable then
  app.alert{ title = "Export Bone Parts", text = msg }
else
  print(msg)
  for _, f in ipairs(report.files) do
    print("  " .. f)
  end
end
