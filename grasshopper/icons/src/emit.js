#!/usr/bin/env node
/* Batch exporter for the OpenPyStruct icon set.
 *
 * Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/Icons/icons/v4/src/emit.js.
 * Copyright (c) Eddy3D Authors, GPL-3.0-or-later. See ../README.md for provenance.
 *
 * The set is authored as code (ops-vec.js + ops-vec-set.js); this turns those definitions into
 * the four artefacts the plugin and the docs consume:
 *
 *     ../svg/<Name>.svg                   one standalone 24x24 SVG per glyph
 *     ../png24/<Name>.png                 the same rasterised at 24x24, transparent
 *     ../OpenPyStruct_Icons_Vector.svg    sprite sheet (one <symbol id="ops-Name">)
 *     ../manifest.csv                     name, ribbon tab, family, accent
 *
 * Adding a glyph is: write the def() in ops-vec-set.js, then
 *
 *     node emit.js Beam_Model         # one or more names
 *     node emit.js                    # the whole set
 *     node emit.js --svg-only Foo     # skip rasterisation
 *     node emit.js --prune            # also drop artefacts with no def()
 *     node emit.js --no-prune         # never drop anything
 *
 * A name that exists as an artefact but has no def() is dead and is removed, measured against the
 * WHOLE definition set, so a targeted run cannot read the rest of the set as dead. A full run
 * prunes; a targeted run reports and leaves it unless --prune says otherwise. Nothing is deleted
 * without being named on stdout.
 *
 * Every glyph goes through the engine's fit()/overflow() safe-area guard: one that busts the
 * 24-unit budget fails the run instead of being silently cropped at render time.
 *
 * Rasterisation shells out to Chromium (rendered at 10x and box-filtered down, which is what gives
 * the 24px PNGs their clean edges). Point OPENPYSTRUCT_CHROME at a binary if it is not on the usual
 * paths, OPENPYSTRUCT_PYTHON at a python with Pillow. With neither present the vector artefacts are
 * still written and only the PNG step is skipped, with a message saying so.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const os = require('os');

const SRC = __dirname;
const ROOT = path.resolve(SRC, '..');
const SVG_DIR = path.join(ROOT, 'svg');
const PNG_DIR = path.join(ROOT, 'png24');
const SPRITE = path.join(ROOT, 'OpenPyStruct_Icons_Vector.svg');
const MANIFEST = path.join(ROOT, 'manifest.csv');

const argv = process.argv.slice(2);
const FLAGS = new Set(['--svg-only', '--prune', '--no-prune']);
const badFlags = argv.filter(a => a.startsWith('--') && !FLAGS.has(a));
if (badFlags.length) {
  console.error('emit: unknown flag ' + badFlags.join(', ') +
    ' — expected one of ' + [...FLAGS].join(', ') + '.');
  process.exit(1);
}
const svgOnly = argv.includes('--svg-only');
const pruneFlag = argv.includes('--prune');
const noPrune = argv.includes('--no-prune');
const names = argv.filter(a => !a.startsWith('--'));

/* The engine is browser-shaped (attaches to window/globalThis), so load it the
   way a page would rather than reshaping it into a module. */
function load(file) {
  // eslint-disable-next-line no-eval
  (0, eval)(fs.readFileSync(path.join(SRC, file), 'utf8'));
}
load('ops-vec.js');
load('ops-vec-set.js');
const V = globalThis.OpsVec;
const SET = globalThis.OpsVecSet;
if (!V || !SET) {
  console.error('emit: the icon engine did not load — run this from src/.');
  process.exit(1);
}

const targets = names.length ? names : SET.ORDER.slice();
const unknown = targets.filter(n => !SET.ICONS[n]);
if (unknown.length) {
  console.error('emit: no def() for ' + unknown.join(', ') +
    ' — add it to ops-vec-set.js first (see README, ADDING A COMPONENT).');
  process.exit(1);
}

fs.mkdirSync(SVG_DIR, { recursive: true });
fs.mkdirSync(PNG_DIR, { recursive: true });

/* ---- pruning ------------------------------------------------------------ */


/* Every OpenPyStruct glyph has a def(), so nothing is exempt from the pruner. Add a name
   here only for a glyph that genuinely has none and must still ship. */
const RESERVED = new Set([]);

/* ORDER is the ribbon ordering and ICONS is the def() registry; a name is only alive if it is
   in one of them (they are the same set today, and a name in neither has no drawing behind it). */
const live = new Set([...SET.ORDER, ...Object.keys(SET.ICONS), ...RESERVED]);

function symbolName(line) {
  const m = line.match(/id="ops-([^"]+)"/);
  return m ? m[1] : null;
}

function spriteNames() {
  if (!fs.existsSync(SPRITE)) return [];
  return fs.readFileSync(SPRITE, 'utf8').split('\n').map(symbolName).filter(Boolean);
}

function manifestNames() {
  if (!fs.existsSync(MANIFEST)) return [];
  return fs.readFileSync(MANIFEST, 'utf8').trimEnd().split('\n').slice(1)
    .filter(l => l.trim()).map(l => l.split(',')[0].trim());
}

function stems(dir, ext) {
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir).filter(f => f.endsWith(ext)).map(f => f.slice(0, -ext.length));
}

/* A name is dead when it exists as an artefact but has no def() behind it. Deadness is measured
   against the WHOLE definition set and NEVER against `targets` — that is what keeps
   `node emit.js One_Name` from reading the other 193 glyphs as dead and wiping the set. */
const deadBy = new Map(); // name -> which artefacts still carry it
for (const [kind, list] of Object.entries({
  svg: stems(SVG_DIR, '.svg'),
  png24: stems(PNG_DIR, '.png'),
  sprite: spriteNames(),
  manifest: manifestNames(),
})) {
  for (const n of list) {
    if (live.has(n)) continue;
    if (!deadBy.has(n)) deadBy.set(n, []);
    deadBy.get(n).push(kind);
  }
}

/* A full run regenerates the whole set, so it prunes; a targeted run only reports, because the
   likelier reading of `node emit.js Foo` is "re-export Foo", not "reconcile the directory". */
const pruning = !noPrune && deadBy.size > 0 && (pruneFlag || names.length === 0);
const dead = pruning ? new Set(deadBy.keys()) : new Set();
const removed = { svg: [], png24: [], sprite: [], manifest: [] };

if (deadBy.size && !pruning) {
  console.log('emit: ' + deadBy.size + ' orphan' + (deadBy.size === 1 ? '' : 's') +
    ' — artefacts with no def() in ops-vec-set.js (a rename usually leaves these):');
  for (const [n, kinds] of deadBy) console.log('    ' + n + '  [' + kinds.join(', ') + ']');
  if (noPrune) console.log('emit: --no-prune given, leaving them in place.');
  else console.log('emit: re-run with --prune, or plain `node emit.js`, to remove them.');
}

for (const n of dead) {
  const svgPath = path.join(SVG_DIR, n + '.svg');
  if (fs.existsSync(svgPath)) { fs.unlinkSync(svgPath); removed.svg.push(n); }
  const pngPath = path.join(PNG_DIR, n + '.png');
  if (fs.existsSync(pngPath)) { fs.unlinkSync(pngPath); removed.png24.push(n); }
}

/* ---- vectors ------------------------------------------------------------ */

const bodies = new Map();
for (const name of targets) {
  const def = SET.ICONS[name];
  const body = V.glyph(def); // draw + fit()
  const over = V.overflow(body);
  if (over) {
    console.error('emit: ' + name + ' exceeds the safe area (' +
      JSON.stringify(over) + '). Shrink the motif — do not bypass the guard.');
    process.exit(1);
  }
  bodies.set(name, body);
  fs.writeFileSync(path.join(SVG_DIR, name + '.svg'), V.wrap(body, 24, true) + '\n');
}
console.log('emit: wrote ' + targets.length + ' SVG' + (targets.length === 1 ? '' : 's'));

/* ---- sprite sheet ------------------------------------------------------- */

function symbolFor(name) {
  return '<symbol id="ops-' + name + '" viewBox="0 0 24 24">' + bodies.get(name) + '</symbol>';
}

if (!fs.existsSync(SPRITE)) {
  fs.writeFileSync(SPRITE, [
    '<svg xmlns="http://www.w3.org/2000/svg" width="0" height="0" style="display:none">',
    ...SET.ORDER.filter(n => bodies.has(n)).map(symbolFor),
    '</svg>'
  ].join('\n') + '\n');
  console.log('emit: sprite sheet created');
} else {
  const lines = fs.readFileSync(SPRITE, 'utf8').split('\n');
  const kept = [];
  for (const line of lines) {
    const n = symbolName(line);
    if (n && targets.includes(n)) continue;                       // rewritten below
    if (n && dead.has(n)) { removed.sprite.push(n); continue; }   // orphaned by a rename
    kept.push(line);
  }
  // Re-insert in ORDER position so the sheet keeps the set's ribbon ordering.
  const out = [];
  const pending = new Set(targets);
  for (const line of kept) {
    const at = SET.ORDER.indexOf(symbolName(line));
    if (at >= 0) {
      for (const n of SET.ORDER.slice(0, at)) {
        if (pending.has(n)) { out.push(symbolFor(n)); pending.delete(n); }
      }
    }
    out.push(line);
  }
  if (pending.size) {
    // Anything after the last existing symbol (e.g. a brand-new tail glyph)
    // goes before the closing </svg>.
    const close = out.lastIndexOf('</svg>');
    const tail = SET.ORDER.filter(n => pending.has(n)).map(symbolFor);
    if (close >= 0) out.splice(close, 0, ...tail);
    else out.push(...tail);
  }
  fs.writeFileSync(SPRITE, out.join('\n'));
  console.log('emit: sprite sheet updated');
}

/* ---- manifest ----------------------------------------------------------- */

if (!fs.existsSync(MANIFEST)) {
  const fresh = SET.ORDER.filter(n => targets.includes(n)).map(name => {
    const def = SET.ICONS[name];
    return [name, SET.TABS[def.tab][0], def.fam, (V.FAM[def.fam] || V.FAM.struct).base].join(',');
  });
  fs.writeFileSync(MANIFEST, ['name,tab,family,accent', ...fresh].join('\n') + '\n');
  console.log('emit: manifest created');
} else {
  const lines = fs.readFileSync(MANIFEST, 'utf8').trimEnd().split('\n');
  const header = lines[0];
  const rows = new Map(lines.slice(1).map(l => [l.split(',')[0], l]));
  for (const n of dead) if (rows.delete(n)) removed.manifest.push(n);
  for (const name of targets) {
    const def = SET.ICONS[name];
    const tab = SET.TABS[def.tab];
    const accent = (V.FAM[def.fam] || V.FAM.struct).base;
    rows.set(name, [name, tab[0], def.fam, accent].join(','));
  }
  const ordered = SET.ORDER.filter(n => rows.has(n)).map(n => rows.get(n));
  // Whatever is left is off-ORDER: after pruning that is only RESERVED, which has no ribbon
  // position, so it trails the ordered block rather than being dropped.
  for (const [n, row] of rows) if (!SET.ORDER.includes(n)) ordered.push(row);
  fs.writeFileSync(MANIFEST, [header, ...ordered].join('\n') + '\n');
  console.log('emit: manifest updated');
}

/* Deletion is never silent: say exactly which name left which artefact. */
const prunedNames = new Set([].concat(...Object.values(removed)));
if (prunedNames.size) {
  console.log('emit: pruned ' + prunedNames.size + ' orphan' + (prunedNames.size === 1 ? '' : 's'));
  for (const n of prunedNames) {
    const from = Object.keys(removed).filter(k => removed[k].includes(n));
    console.log('    - ' + n + '  [' + from.join(', ') + ']');
  }
}

if (svgOnly) process.exit(0);

/* ---- rasterisation ------------------------------------------------------ */

function findChrome() {
  const candidates = [process.env.OPENPYSTRUCT_CHROME];
  const pw = '/opt/pw-browsers';
  if (fs.existsSync(pw)) {
    for (const d of fs.readdirSync(pw)) {
      candidates.push(path.join(pw, d, 'chrome-linux', 'chrome'));
      candidates.push(path.join(pw, d, 'chrome-mac', 'Chromium.app', 'Contents', 'MacOS', 'Chromium'));
    }
  }
  candidates.push(
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/Applications/Chromium.app/Contents/MacOS/Chromium',
    '/usr/bin/chromium', '/usr/bin/chromium-browser', '/usr/bin/google-chrome',
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe');
  return candidates.find(c => c && fs.existsSync(c)) || null;
}

function findPython() {
  const candidates = [process.env.OPENPYSTRUCT_PYTHON, 'python3', 'python'];
  for (const candidate of candidates) {
    if (!candidate) continue;
    try {
      execFileSync(candidate, ['-c', 'import PIL'], { stdio: 'ignore' });
      return candidate;
    } catch { /* try the next interpreter */ }
  }
  return null;
}

const chrome = findChrome();
const python = findPython();
if (!chrome) {
  console.log('emit: skipping PNGs — no Chromium found (set OPENPYSTRUCT_CHROME).' +
    ' The SVGs, sprite sheet and manifest are up to date.');
  process.exit(0);
}

const SS = 10;               // supersample factor: 24px * 10
const SIZE = 24 * SS;
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'ops-icons-'));

/* Pillow does three things: crop the oversized screenshot, reject a blank render,
   and box-filter 240 -> 24. Chromium can do all three in-page, so a machine without
   python3 still gets PNGs rather than a half-exported glyph — the trap being that a
   missing PNG is INVISIBLE (Icons.For returns null and Grasshopper just draws
   its default box). The canvas path halves repeatedly before the last step because a
   single 240 -> 24 drawImage samples too sparsely and thins the 0.85 hairlines;
   successive halving is the same averaging Pillow's LANCZOS gives us. Pillow stays
   the default so the 180-odd glyphs already in png24/ keep their exact pipeline. */
function rasterizeWithChromeCanvas(name, svg, outPath) {
  const page = '<!doctype html><html><body><script>\n' +
    'var SVG = ' + JSON.stringify(svg) + ', S = ' + SIZE + ';\n' +
    'function done(t) { var d = document.createElement("div"); d.id = "out";\n' +
    '  d.textContent = t; document.body.appendChild(d); }\n' +
    'var img = new Image();\n' +
    'img.onload = function () {\n' +
    '  var big = document.createElement("canvas"); big.width = big.height = S;\n' +
    '  var bc = big.getContext("2d"); bc.drawImage(img, 0, 0, S, S);\n' +
    '  var px = bc.getImageData(0, 0, S, S).data, max = 0;\n' +
    '  for (var i = 3; i < px.length; i += 4 * 7) if (px[i] > max) max = px[i];\n' +
    '  if (max < 40) return done("BLANK");\n' +
    '  var cur = big, size = S;\n' +
    '  while (size / 2 >= 24) {\n' +
    '    var half = document.createElement("canvas"); half.width = half.height = size / 2;\n' +
    '    var hc = half.getContext("2d");\n' +
    '    hc.imageSmoothingEnabled = true; hc.imageSmoothingQuality = "high";\n' +
    '    hc.drawImage(cur, 0, 0, size / 2, size / 2); cur = half; size /= 2;\n' +
    '  }\n' +
    '  var out = document.createElement("canvas"); out.width = out.height = 24;\n' +
    '  var oc = out.getContext("2d");\n' +
    '  oc.imageSmoothingEnabled = true; oc.imageSmoothingQuality = "high";\n' +
    '  oc.drawImage(cur, 0, 0, 24, 24);\n' +
    '  done(out.toDataURL("image/png"));\n' +
    '};\n' +
    'img.onerror = function () { done("ERROR"); };\n' +
    'img.src = "data:image/svg+xml;base64," + btoa(unescape(encodeURIComponent(SVG)));\n' +
    '<\/script></body></html>';
  const pagePath = path.join(tmp, name + '-canvas.html');
  fs.writeFileSync(pagePath, page);
  // --virtual-time-budget lets the async image decode complete before the dump;
  // --dump-dom is how the base64 comes back without speaking CDP.
  const dom = execFileSync(chrome, [
    '--headless', '--no-sandbox', '--disable-gpu', '--hide-scrollbars',
    '--virtual-time-budget=10000', '--dump-dom', 'file://' + pagePath,
  ], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'ignore'] });
  const match = dom.match(/data:image\/png;base64,([A-Za-z0-9+/=]+)/);
  if (!match) {
    throw new Error('rasterising ' + name + ' via Chromium canvas failed' +
      (/>BLANK</.test(dom) ? ' (rendered blank)' : '') + '.');
  }
  fs.writeFileSync(outPath, Buffer.from(match[1], 'base64'));
}

for (const name of targets) {
  const svg = fs.readFileSync(path.join(SVG_DIR, name + '.svg'), 'utf8');
  const outPath = path.join(PNG_DIR, name + '.png');
  if (!python) {
    rasterizeWithChromeCanvas(name, svg, outPath);
    continue;
  }

  // A bare .svg file and a tiny viewport both render blank in headless Chrome;
  // an HTML wrapper with a generously oversized window avoids both.
  const html = '<!doctype html><html><head><style>*{margin:0;padding:0}' +
    'svg{width:' + SIZE + 'px;height:' + SIZE + 'px;display:block}</style></head><body>' +
    svg + '</body></html>';
  const htmlPath = path.join(tmp, name + '.html');
  const shotPath = path.join(tmp, name + '.png');
  fs.writeFileSync(htmlPath, html);
  execFileSync(chrome, [
    '--headless', '--no-sandbox', '--disable-gpu', '--hide-scrollbars',
    '--default-background-color=00000000',
    '--screenshot=' + shotPath,
    '--window-size=' + (SIZE + 40) + ',' + (SIZE + 200),
    'file://' + htmlPath,
  ], { stdio: 'ignore' });

  execFileSync(python, ['-c', [
    'import sys',
    'from PIL import Image',
    'src, dst, size = sys.argv[1], sys.argv[2], int(sys.argv[3])',
    'im = Image.open(src).convert("RGBA").crop((0, 0, size, size))',
    'if max(im.getpixel((x, y))[3] for x in range(size) for y in range(0, size, 7)) < 40:',
    '    raise SystemExit("rendered blank: " + src)',
    'im.resize((24, 24), Image.LANCZOS).save(dst)',
  ].join('\n'), shotPath, outPath, String(SIZE)], { stdio: 'inherit' });
}

fs.rmSync(tmp, { recursive: true, force: true });
console.log('emit: rasterised ' + targets.length + ' PNG' + (targets.length === 1 ? '' : 's') +
  (python ? '' : ' (Chromium canvas — python3 + Pillow not available)'));
