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
--
-- Batch use (no UI), e.g. from CI or an agent:
--   Aseprite.exe -b "file.aseprite" --script-param group="Human (128x128)" \
--                --script "Aseprite/scripts/export_bone_parts.lua"
-- Params: group (export only that group's subtree), scale (nearest, default 10),
--         subdir (output folder name instead of the .aseprite basename).
-- Without `group` a workbench file exports everything on it — old blockouts and
-- reference boards included, which is rarely what you want.

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

--- Сообщить об отказе так, чтобы это было видно и в batch: app.alert без UI
--- молчит, и скрипт выглядит «просто ничего не сделал».
local function fail(msg)
  print("export_bone_parts: " .. msg)
  if app.isUIAvailable then
    app.alert(msg)
  end
end

local function findProjectRoot(startFile)
  local dir = app.fs.filePath(startFile)
  -- В CLI имя файла приходит таким, как его передали: относительный путь
  -- уводит поиск в пустоту, поэтому достраиваем его от текущей папки.
  -- Проверяем строкой, а не app.fs.isAbsolutePath — этого поля нет в части
  -- версий API, и обращение к нему роняет скрипт целиком.
  local looksAbsolute = dir:match("^%a:[\\/]") ~= nil or dir:match("^[\\/]") ~= nil
  if not looksAbsolute then
    local ok, cwd = pcall(function() return app.fs.currentPath end)
    if ok and cwd ~= nil and cwd ~= "" then
      dir = app.fs.joinPath(cwd, dir)
    end
  end
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

--- Найти группу по имени в любой вложенности (для --script-param group=…).
local function findGroup(layers, wanted)
  for i = 1, #layers do
    local layer = layers[i]
    if layer.isGroup then
      if layer.name == wanted then
        return layer
      end
      local nested = findGroup(layer.layers, wanted)
      if nested then
        return nested
      end
    end
  end
  return nil
end

local function collectAndExport(sprite, frame, outDir, scale, report, roots)
  -- Имя PNG = имя слоя, но у левой и правой конечности слои-листья названы
  -- одинаково ("Leg (Down)" в обеих группах), и второй файл затирал первый
  -- молча. При коллизии дописываем имя родительской группы; для файлов без
  -- коллизий имя не меняется, чтобы не рвать ссылки в уже собранных ригах.
  local taken = {}

  local function uniqueName(name, parentName)
    if not taken[name] then
      return name
    end
    if parentName == nil or parentName == "" then
      return name .. " (2)"
    end
    local candidate = parentName .. " - " .. name
    local n = 2
    while taken[candidate] do
      candidate = parentName .. " - " .. name .. " (" .. n .. ")"
      n = n + 1
    end
    return candidate
  end

  local function walk(layers, parentName)
    for i = 1, #layers do
      local layer = layers[i]
      local name = layer.name

      if shouldSkip(name) then
        report.skipped = report.skipped + 1
      elseif layer.isGroup and startsWith(name, "@") then
        local exportName = uniqueName(cleanExportName(name), parentName)
        taken[exportName] = true
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
        walk(layer.layers, name)
      elseif layer.isImage then
        local cel = layer:cel(frame)
        if not cel or not cel.image then
          report.empty = report.empty + 1
          table.insert(report.warnings, name .. " (no cel)")
        else
          -- Use cel image directly (already content-sized); trim again for safety
          local exportName = uniqueName(name, parentName)
          taken[exportName] = true
          local outPath = app.fs.joinPath(outDir, exportName .. ".png")
          local ok, err = exportImage(cel.image, outPath, scale, sprite)
          if ok then
            report.exported = report.exported + 1
            table.insert(report.files, exportName .. ".png")
          else
            report.empty = report.empty + 1
            table.insert(report.warnings, exportName .. " (" .. tostring(err) .. ")")
          end
        end
      end
    end
  end

  walk(roots or sprite.layers, nil)
end

---------------------------------------------------------------------------
-- Entry
---------------------------------------------------------------------------

local sprite = app.activeSprite
if not sprite then
  fail("No active sprite. Open a .aseprite file first.")
  return
end

if not sprite.filename or sprite.filename == "" then
  fail("Sprite is unsaved. Save the .aseprite into the project Aseprite/ folder first.")
  return
end

local projectRoot = findProjectRoot(sprite.filename)
if not projectRoot then
  fail("Cannot find project root (Assets/_Project). "
    .. "Save the .aseprite under the repo, e.g. Aseprite/Bone Animations/.")
  return
end

local params = app.params or {}
local basename = app.fs.fileTitle(sprite.filename)
local subdir = params["subdir"]
if subdir == nil or subdir == "" then
  subdir = basename
end
local outDir = app.fs.joinPath(projectRoot, OUTPUT_REL, subdir)
local defaultScale = tonumber(params["scale"]) or DEFAULT_SCALE

-- Фильтр по группе: на верстаке рядом с рабочей фигурой лежат снятые болванки
-- и реф-борды, и без фильтра они уедут в проект вместе с ней.
local roots = nil
local groupName = params["group"]
if groupName ~= nil and groupName ~= "" then
  local group = findGroup(sprite.layers, groupName)
  if not group then
    local msg = "Group not found: " .. groupName
    if app.isUIAvailable then
      return app.alert(msg)
    end
    print(msg)
    return
  end
  roots = group.layers
end

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

collectAndExport(sprite, frame, outDir, scale, report, roots)

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
