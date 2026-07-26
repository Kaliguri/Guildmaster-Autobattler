# -*- coding: utf-8 -*-
"""
Генератор `FMOD Project/Tooling/populate.js` из `FMOD Project/Scripts/manifest.json`.

populate.js — идемпотентный скрипт заливки: строит шины, события, инструменты, микс
(громкости, рандомизация, voice-макросы), глобальный параметр TimeScale и его кривую питча.

Запуск:
    python scripts/audio/build_populate.py
    "C:/Program Files/FMOD SoundSystem/FMOD Studio 2.03.14/fmodstudiocl.exe" \
        -script "FMOD Project/Tooling/populate.js" "FMOD Project/Guildmaster Autobattler Game.fspro"
    "...fmodstudiocl.exe" -build -ignore-warnings -export-guids "FMOD Project/Guildmaster Autobattler Game.fspro"
"""
import json
import os
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
MANIFEST = os.path.join(REPO, "FMOD Project", "Scripts", "manifest.json")
OUT = os.path.join(REPO, "FMOD Project", "Tooling", "populate.js")
LOG = (REPO.replace("\\", "/") + "/FMOD Project/Scripts/populate_log.txt")

HEADER = """// =============================================================================
// Guildmaster — заливка звука в FMOD Studio. СГЕНЕРИРОВАН scripts/audio/build_populate.py
// из FMOD Project/Scripts/manifest.json. Правь карту (scripts/audio/audio_map.py) и
// перегенерируй — руками этот файл не трогать.
//
// Что делает (spec: docs/wiki/tech/40-planning/sfx-round-2.md):
//   1. Шины: bus:/SFX{Combat,UI,Ambient,Stingers} и bus:/Music — под слайдеры настроек.
//   2. События из манифеста: мульти-инструмент (round-robin), банк, роутинг в под-шину.
//   3. Микс: категорийный offset громкости, рандомизация питча/громкости (анти-«пулемёт»),
//      voice-макросы maxVoices/stealing/cooldown/priority (анти-каша плотного боя).
//   4. Глобальный параметр TimeScale + кривая питча bus:/SFX/Combat (slowmo слышен).
//
// Идемпотентен: повторный прогон пересобирает события и обновляет шины, не плодя дубли.
// Headless-safe: никаких модальных диалогов, весь вывод в populate_log.txt.
//
// RUN (две команды — заливка, потом сборка банков):
//   fmodstudiocl.exe -script "FMOD Project/Tooling/populate.js" "FMOD Project/Guildmaster Autobattler Game.fspro"
//   fmodstudiocl.exe -build -ignore-warnings -export-guids "FMOD Project/Guildmaster Autobattler Game.fspro"
// =============================================================================

(function () {
    'use strict';

    var LOG_PATH = "%LOG%";
    var REPO_ROOT = "%REPO%";
    var MANIFEST = %MANIFEST%;
"""

BODY = r"""
    var logLines = [];
    function log(status, subject, reason) {
        logLines.push(status + ": " + subject + (reason ? " (" + reason + ")" : ""));
    }
    function flush() {
        try {
            var f = studio.system.getFile(LOG_PATH);
            f.open(studio.system.openMode.WriteOnly);
            f.writeText(logLines.join("\n") + "\n");
            f.close();
        } catch (e) { /* headless: больше ничего не сделать */ }
    }

    function findByName(modelType, name) {
        var all = modelType.findInstances();
        for (var i = 0; i < all.length; i++) if (all[i].name === name) return all[i];
        return null;
    }

    function findOrCreateBank(name) {
        var b = findByName(studio.project.model.Bank, name);
        if (b) return b;
        b = studio.project.create("Bank");
        b.name = name;
        log("BANK", name, "created");
        return b;
    }

    // --- 1. Шины -------------------------------------------------------------
    // Имя шины в дереве — путь ("SFX/Combat"); в проекте это MixerGroup "Combat" внутри "SFX".
    var busByPath = {};
    function busLeafName(path) { var p = path.split("/"); return p[p.length - 1]; }

    // Сносим наши шины целиком и строим заново: сравнивать MixerGroup по ссылке нельзя
    // (===  на ManagedObject не работает), а lookup по пути на дублях врёт. События всё равно
    // пересоздаются ниже, так что роутинг не теряется.
    function purgeManagedBuses() {
        var groups = studio.project.model.MixerGroup.findInstances();
        var killed = 0;
        for (var i = 0; i < groups.length; i++) {
            var p;
            try { p = groups[i].getPath(); } catch (e) { continue; }
            for (var busPath in MANIFEST.buses) {
                if (!MANIFEST.buses.hasOwnProperty(busPath)) continue;
                var root = busPath.split("/")[0];
                if (p === "bus:/" + root || p.indexOf("bus:/" + root + "/") === 0) {
                    studio.project.deleteObject(groups[i]);
                    killed++;
                    break;
                }
            }
        }
        return killed;
    }

    function ensureBus(path, spec, master) {
        if (busByPath[path]) return busByPath[path];
        var parent = spec.parent ? ensureBus(spec.parent, MANIFEST.buses[spec.parent], master) : master;
        var g = studio.project.create("MixerGroup");
        g.name = busLeafName(path);
        g.output = parent;
        try { g.volume = spec.volumeDb; } catch (e) { log("WARN", path, "volume failed: " + e); }
        log("BUS", path, "created " + g.getPath());
        busByPath[path] = g;
        return g;
    }

    // --- 2. Папки событий ----------------------------------------------------
    function ensureEventFolder(folderPath) {
        var parent = studio.project.workspace.masterEventFolder;
        if (!folderPath) return parent;
        var parts = folderPath.split("/");
        for (var i = 0; i < parts.length; i++) {
            var name = parts[i], found = null;
            var kids = parent.items || [];
            for (var k = 0; k < kids.length; k++) {
                if (kids[k].isOfExactType && kids[k].isOfExactType("EventFolder") && kids[k].name === name) { found = kids[k]; break; }
            }
            if (!found) { found = studio.project.create("EventFolder"); found.name = name; found.folder = parent; }
            parent = found;
        }
        return parent;
    }

    function parseEventPath(path) {
        var rel = path.replace(/^event:\//, "");
        var i = rel.lastIndexOf("/");
        return { folder: i !== -1 ? rel.substring(0, i) : "", name: i !== -1 ? rel.substring(i + 1) : rel };
    }

    // ГОТЧА: у ManagedObject нет свойства isDestroyed — присваивание молча создаёт JS-поле и ничего
    // не удаляет (так раунд 1 плодил дубли). Удаляет только studio.project.deleteObject().
    // Чистим ВСЕ события внутри наших папок, которых нет в манифесте: populate — единственный
    // источник этих событий, ручных правок тут не держим.
    function purgeManagedEvents(keepPaths) {
        var events = studio.project.model.Event.findInstances();
        var killed = 0;
        for (var i = 0; i < events.length; i++) {
            var p;
            try { p = events[i].getPath(); } catch (e) { continue; }
            var managed = p.indexOf("event:/SFX/") === 0 || p.indexOf("event:/Stingers/") === 0 || p.indexOf("event:/Music/") === 0;
            if (!managed) continue;
            if (keepPaths[p]) continue;   // это событие пересоздаётся ниже — его снесёт rebuild
            studio.project.deleteObject(events[i]);
            killed++;
        }
        return killed;
    }

    function destroyExistingEvent(fullPath) {
        var events = studio.project.model.Event.findInstances();
        var killed = 0;
        for (var i = 0; i < events.length; i++) {
            var p;
            try { p = events[i].getPath(); } catch (e) { continue; }
            if (p === fullPath) { studio.project.deleteObject(events[i]); killed++; }
        }
        return killed;
    }

    // Ассеты, на которые больше никто не ссылается, — из проекта вон: иначе FMOD Project/Assets
    // копит мусор от прошлых прогонов (каждый импорт кладёт туда копию файла).
    function destroyUnusedAssets() {
        var files = studio.project.model.AudioFile.findInstances();
        var killed = 0;
        for (var i = 0; i < files.length; i++) {
            var f = files[i], refs = -1;
            try { refs = (f.sounds || []).length + (f.programmerSounds || []).length + (f.dataReferees || []).length; }
            catch (e) { continue; }
            if (refs === 0) { studio.project.deleteObject(f); killed++; }
        }
        return killed;
    }

    try {
        var master = studio.project.workspace.mixer.masterBus;
        log("STARTED", "populate", "master=" + master.getPath());
        flush();

        log("PURGE", "managed buses", purgeManagedBuses() + " removed");
        for (var busPath in MANIFEST.buses) {
            if (MANIFEST.buses.hasOwnProperty(busPath)) ensureBus(busPath, MANIFEST.buses[busPath], master);
        }
        flush();

        var sfxBank = findOrCreateBank(MANIFEST.bank || "SFX");
        var musicBank = findOrCreateBank(MANIFEST.musicBank || "Music");

        // --- 3. Глобальный параметр TimeScale --------------------------------
        var tsSpec = MANIFEST.timeScaleParam;
        var tsParam = null;
        if (tsSpec) {
            var preset = findByName(studio.project.model.ParameterPreset, tsSpec.name);
            if (!preset) {
                var created = studio.project.workspace.addGameParameter({ name: tsSpec.name });
                preset = findByName(studio.project.model.ParameterPreset, tsSpec.name);
                log("PARAM", tsSpec.name, "created");
            }
            if (preset && preset.parameter) {
                tsParam = preset.parameter;
                try {
                    tsParam.minimum = tsSpec.minimum;
                    tsParam.maximum = tsSpec.maximum;
                    tsParam.initialValue = tsSpec.initial;
                    tsParam.isGlobal = true;
                    log("PARAM", tsSpec.name, "range " + tsParam.minimum + ".." + tsParam.maximum + " global=" + tsParam.isGlobal);
                } catch (e) { log("WARN", tsSpec.name, "param props failed: " + e); }
            }
        }

        // Кривая питча боевой шины по TimeScale: slowmo/ускорение слышно.
        if (tsParam && tsSpec && busByPath[tsSpec.bus]) {
            var bus = busByPath[tsSpec.bus];
            var hasCurve = false;
            try {
                var autos = bus.automators || [];
                for (var a = 0; a < autos.length; a++) if (autos[a].nameOfPropertyBeingAutomated === "pitch") hasCurve = true;
            } catch (e) {}
            if (!hasCurve) {
                try {
                    var automator = bus.addAutomator("pitch");
                    var curve = automator.addAutomationCurve(tsParam);
                    for (var c = 0; c < tsSpec.curve.length; c++) {
                        curve.addAutomationPoint(tsSpec.curve[c][0], tsSpec.curve[c][1]);
                    }
                    log("AUTOMATION", tsSpec.bus + ".pitch", tsSpec.curve.length + " points on " + tsSpec.name);
                } catch (e) { log("WARN", "TimeScale curve", "failed: " + e); }
            } else {
                log("AUTOMATION", tsSpec.bus + ".pitch", "already present");
            }
        }
        flush();

        // --- 4. События ------------------------------------------------------
        var events = MANIFEST.events || [];
        var built = 0, failed = 0;

        var keep = {};
        for (var kp = 0; kp < events.length; kp++) keep[events[kp].path] = true;
        log("PURGE", "stale events", purgeManagedEvents(keep) + " removed");
        flush();

        for (var e = 0; e < events.length; e++) {
            var entry = events[e];
            var cat = MANIFEST.categories[entry.category] || {};
            var parsed = parseEventPath(entry.path);
            var folder = ensureEventFolder(parsed.folder);
            destroyExistingEvent(entry.path);

            var event = studio.project.create("Event");
            event.name = parsed.name;
            event.folder = folder;
            var track = event.addGroupTrack();

            var assets = [];
            for (var fi = 0; fi < entry.files.length; fi++) {
                var full = REPO_ROOT + "/" + MANIFEST.sourceRoot + "/" + entry.files[fi];
                var asset = studio.project.importAudioFile(full);
                if (asset) assets.push(asset); else log("FAILED", full, "import failed");
            }
            if (assets.length === 0) { event.isDestroyed = true; failed++; log("FAILED", entry.path, "no audio"); flush(); continue; }

            var length = 0;
            for (var m = 0; m < assets.length; m++) length = Math.max(length, assets[m].length || 1);
            if (length <= 0) length = 1;

            var instrument;
            if (assets.length > 1) {
                instrument = track.addSound(event.timeline, "MultiSound", 0, length);
                instrument.name = parsed.name;
                for (var s = 0; s < assets.length; s++) {
                    var single = studio.project.create("SingleSound");
                    single.audioFile = assets[s];
                    single.owner = instrument;
                }
            } else {
                instrument = track.addSound(event.timeline, "SingleSound", 0, length);
                instrument.audioFile = assets[0];
                instrument.name = parsed.name;
            }

            // Луп (музыка/амбиент): инструмент крутится, событие persistent — иначе оно само себя остановит.
            if (cat.looping) {
                try {
                    instrument.looping = true;
                    instrument.playCount = 0;
                    event.automatableProperties.isPersistent = true;
                    track.streaming = true;   // музыка/амбиент не грузятся в память целиком
                } catch (le) { log("WARN", entry.path, "looping failed: " + le); }
            }

            // Рандомизация: против «пулемётного» повтора одного сэмпла.
            if (cat.pitchSt) {
                try { instrument.addModulator("RandomizerModulator", "pitch").amount = cat.pitchSt; }
                catch (pe) { log("WARN", entry.path, "pitch modulator failed: " + pe); }
            }
            if (cat.volRandDb) {
                try { instrument.addModulator("RandomizerModulator", "volume").amount = cat.volRandDb; }
                catch (ve) { log("WARN", entry.path, "volume modulator failed: " + ve); }
            }

            // Банк + роутинг в под-шину + категорийная громкость.
            try { event.relationships.banks.add(cat.looping ? musicBank : sfxBank); }
            catch (be) { log("WARN", entry.path, "bank assign failed: " + be); }

            var targetBus = busByPath[cat.bus];
            if (targetBus) {
                try { event.mixerInput.output = targetBus; }
                catch (re) { log("WARN", entry.path, "route failed: " + re); }
            } else {
                log("WARN", entry.path, "bus not found: " + cat.bus);
            }
            try { event.mixerInput.volume = cat.volumeDb || 0; }
            catch (vo) { log("WARN", entry.path, "event volume failed: " + vo); }

            // Voice-макросы: сколько одновременных копий, кого душить и как часто пускать.
            try {
                var ap = event.automatableProperties;
                if (cat.maxVoices) ap.maxVoices = cat.maxVoices;
                if (cat.stealing !== undefined) ap.voiceStealing = cat.stealing;
                if (cat.priority !== undefined) ap.priority = cat.priority;
                if (cat.cooldownMs) ap.triggerCooldown = cat.cooldownMs / 1000.0;
            } catch (me) { log("WARN", entry.path, "macros failed: " + me); }

            built++;
            log("OK", entry.path, assets.length + " file(s), cat=" + entry.category + ", bus=" + cat.bus);
            if (built % 20 === 0) flush();
        }

        var purged = destroyUnusedAssets();
        log("CLEANUP", "unused assets", purged + " removed");

        log("SAVING", "project", "");
        flush();
        studio.project.save();
        log("DONE", "populate", "built " + built + ", failed " + failed + " — теперь fmodstudiocl -build -ignore-warnings -export-guids");
        flush();
    } catch (err) {
        log("FATAL", "populate", "" + err);
        flush();
    }
})();
"""


def main():
    with open(MANIFEST, encoding="utf-8") as fh:
        manifest = json.load(fh)
    js = HEADER.replace("%LOG%", LOG).replace("%REPO%", REPO.replace("\\", "/")) \
               .replace("%MANIFEST%", json.dumps(manifest, ensure_ascii=False, indent=2)) + BODY
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(js)
    print(f"populate.js: {len(manifest['events'])} событий, {len(manifest['buses'])} шин -> {os.path.relpath(OUT, REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
