/* OpenPyStruct icon engine. Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D),
   GUI/Icons/icons/v4/src/eddy-vec.js. Copyright (c) Eddy3D Authors, GPL-3.0-or-later.
   See ../README.md for provenance. Changes on copy: the global is OpsVec, a Structure
   family was added to FAM, an unknown family falls back to Structure rather than Airflow,
   and the structural motifs at the end of the motif section are new. */
/* Eddy3D icon engine — VECTOR.
   The raster engine (eddy-draw.js) places squares on a 24x24 grid: correct for a
   fixed 24px bitmap, but it is pixels, so it looks like pixels the moment anything
   scales. A native Grasshopper vector set is line art — bezier and arc paths with a
   single stroke weight, anti-aliased by the renderer. This engine draws that instead.

   RULES
     space      24 x 24 user units, viewBox "0 0 24 24". Live area 2..22.
     stroke     TWO weights only: 1.25u for every contour, 0.85u for interior
                hairlines (grid rules, node links, chip pins, panel dividers).
                Round caps and joins, #14181c. Centred on the path; scales with the icon.
     tint       A filled detail narrower than ~3.5u is stroked in its OWN colour,
                not ink — a 1.25 contour on both sides would consume it.
     fill       white for built volume, family accent for what flows or radiates.
     angles     unrestricted — curves are curves. No pixel snapping, no 2:1 stair.
     isometric  true 30 deg (0.866 : 0.5).
     shadow     none by default. Vector icons are rendered live; the legacy raster
                drop shadow exists only to fake depth in a 24px bitmap.
     output     one <symbol> per component in a single sprite sheet, plus a
                standalone <svg> per component. */
(function (root) {
  var SW = 1.25, HAIR = 0.85, INK = '#14181c';

  var FAM = {
    tool: { base: '#6b7580', lite: '#dde1e5', dark: '#3c444c', name: 'Tooling' },
    air:  { base: '#2e9bd6', lite: '#cfe9fb', dark: '#1a6c9c', name: 'Airflow' },
    heat: { base: '#e4572e', lite: '#ffdccf', dark: '#a32f13', name: 'Thermal' },
    ml:   { base: '#7a5af5', lite: '#e4dcff', dark: '#4b32b0', name: 'Prediction' },
    land: { base: '#4e9a51', lite: '#d9efd7', dark: '#2e6330', name: 'Urban fabric' },
    haz:  { base: '#c2185b', lite: '#fbd0de', dark: '#7b0f3b', name: 'Contaminant' },
    /* Surface water. A seventh hue rather than borrowing Airflow's blue: hue means PHYSICS
       here, and runoff on a graded surface is a different one from wind. Teal reads as water
       at 24px and stays clearly apart from #2e9bd6, which sky blue would not. */
    water: { base: '#0f8b8d', lite: '#cfeceb', dark: '#076062', name: 'Surface water' },
    /* Structure. An eighth hue, for the same reason Surface water is a seventh: hue means
       PHYSICS, and statics is not one of the seven above. Amber sits in the empty ~40deg gap
       of the wheel, stays apart from Thermal's #e4572e at 24px, and reads as structural steel.
       The families Eddy3D defines are kept so this engine can still be re-synced with it, and
       so a component that is genuinely one of them can say so. */
    struct: { base: '#b5821f', lite: '#f6e6c6', dark: '#7a5510', name: 'Structure' }
  };

  function attrs(o) {
    o = o || {};
    var a = [];
    a.push('fill="' + (o.fill || 'none') + '"');
    if (o.stroke !== false) {
      a.push('stroke="' + (o.tint ? (o.fill || INK) : (o.stroke || INK)) + '"');
      a.push('stroke-width="' + (o.sw || SW) + '"');
    }
    if (o.op) a.push('opacity="' + o.op + '"');
    if (o.dash) a.push('stroke-dasharray="' + o.dash + '"');
    return a.join(' ');
  }
  function P(d, o) { return '<path d="' + d + '" ' + attrs(o) + '/>'; }
  function circle(cx, cy, r, o) { return '<circle cx="' + cx + '" cy="' + cy + '" r="' + r + '" ' + attrs(o) + '/>'; }
  function ellipse(cx, cy, rx, ry, o) { return '<ellipse cx="' + cx + '" cy="' + cy + '" rx="' + rx + '" ry="' + ry + '" ' + attrs(o) + '/>'; }
  function rect(x, y, w, h, r, o) { return '<rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" rx="' + (r || 0) + '" ' + attrs(o) + '/>'; }
  function line(x1, y1, x2, y2, o) { return '<path d="M' + x1 + ' ' + y1 + 'L' + x2 + ' ' + y2 + '" ' + attrs(o) + '/>'; }
  function poly(pts, o, close) {
    var d = 'M' + pts.map(function (p) { return p[0] + ' ' + p[1]; }).join('L') + (close === false ? '' : 'Z');
    return P(d, o);
  }
  function g(children, tf) { return '<g' + (tf ? ' transform="' + tf + '"' : '') + '>' + children.join('') + '</g>'; }

  /* ---------- shared motifs ---------- */

  /* true 30deg isometric box. cx,cy = centre; r = half-width; h = body height */
  function isoBox(cx, cy, r, h, f) {
    var k = 0.866 * r, m = r / 2, out = [];
    /* top face */
    out.push(poly([[cx, cy - m - h / 2], [cx + k, cy - h / 2], [cx, cy + m - h / 2], [cx - k, cy - h / 2]], { fill: f.top }));
    /* left face */
    out.push(poly([[cx - k, cy - h / 2], [cx, cy + m - h / 2], [cx, cy + m + h / 2], [cx - k, cy + h / 2]], { fill: f.left }));
    /* right face */
    out.push(poly([[cx + k, cy - h / 2], [cx, cy + m - h / 2], [cx, cy + m + h / 2], [cx + k, cy + h / 2]], { fill: f.right }));
    return g(out);
  }
  function isoGhost(cx, cy, r, h, c) {
    var k = 0.866 * r, m = r / 2;
    return g([
      poly([[cx, cy - m - h / 2], [cx + k, cy - h / 2], [cx + k, cy + h / 2], [cx, cy + m + h / 2], [cx - k, cy + h / 2], [cx - k, cy - h / 2]], { stroke: c, dash: '2 1.6', sw: HAIR }),
      line(cx, cy + m - h / 2, cx, cy + m + h / 2, { stroke: c, dash: '2 1.6', sw: HAIR })
    ]);
  }
  /* cylinder with real elliptical caps */
  function cyl(cx, cy, rx, h, f) {
    var ry = rx * 0.34, t = cy - h / 2, b = cy + h / 2;
    return g([
      P('M' + (cx - rx) + ' ' + t + 'L' + (cx - rx) + ' ' + b +
        'A' + rx + ' ' + ry + ' 0 0 0 ' + (cx + rx) + ' ' + b +
        'L' + (cx + rx) + ' ' + t + 'Z', { fill: f.body }),
      ellipse(cx, t, rx, ry, { fill: f.top })
    ]);
  }
  /* geometry helpers — shared so a surface and its mesh cannot drift apart */
  function pt(a) { return a[0].toFixed(2) + ' ' + a[1].toFixed(2); }
  function lerp(a, b, t) { return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t]; }
  function qAt(p0, c, p1, t) {
    var u = 1 - t;
    return [u * u * p0[0] + 2 * u * t * c[0] + t * t * p1[0],
      u * u * p0[1] + 2 * u * t * c[1] + t * t * p1[1]];
  }
  /* smooth polyline through points, midpoint-quadratic */
  function smooth(ps) {
    if (ps.length < 3) return 'M' + ps.map(pt).join('L');
    var d = 'M' + pt(ps[0]), i;
    for (i = 1; i < ps.length - 1; i++) d += 'Q' + pt(ps[i]) + ' ' + pt(lerp(ps[i], ps[i + 1], 0.5));
    return d + 'Q' + pt(ps[ps.length - 1]) + ' ' + pt(ps[ps.length - 1]);
  }

  /* a surface / brep: a plane seen in space. Both long edges bow equally, so the
     quad stays parallel-sided — an unequal sag reads as a flag, not a plane. */
  function srfGeom(x, y, w, h) {
    var s = w * 0.14;
    return {
      tl: [x, y + s], cT: [x + w * 0.5, y - s * 0.75], tr: [x + w, y],
      br: [x + w, y + h], cB: [x + w * 0.5, y + h + s * 1.75], bl: [x, y + h + s]
    };
  }
  function surface(x, y, w, h, f) {
    var q = srfGeom(x, y, w, h);
    return P('M' + pt(q.tl) + 'Q' + pt(q.cT) + ' ' + pt(q.tr) +
      'L' + pt(q.br) + 'Q' + pt(q.cB) + ' ' + pt(q.bl) + 'Z', { fill: f });
  }
  /* the same surface with its mesh ON it: interior lines are sampled from the very
     curves that form the outline, so every intersection lands on the plane. */
  function meshSurface(x, y, w, h, f, nu, nv, c) {
    var q = srfGeom(x, y, w, h), out = [surface(x, y, w, h, f)], i, t, a, b;
    for (i = 1; i < nv; i++) {
      t = i / nv;
      a = qAt(q.tl, q.cT, q.tr, t); b = qAt(q.bl, q.cB, q.br, t);
      out.push(line(a[0], a[1], b[0], b[1], { stroke: c, sw: HAIR }));
    }
    for (i = 1; i < nu; i++) {
      t = i / nu;
      out.push(P('M' + pt(lerp(q.tl, q.bl, t)) + 'Q' + pt(lerp(q.cT, q.cB, t)) +
        ' ' + pt(lerp(q.tr, q.br, t)), { stroke: c, sw: HAIR }));
    }
    return g(out);
  }
  /* a point ON a surface — u across, v down the plane. Details placed through this
     sit on the geometry, the way srfDots' probe grid does, instead of floating over
     it on a straight lattice. */
  function srfPoint(x, y, w, h, u, v) {
    var q = srfGeom(x, y, w, h);
    return qAt(lerp(q.tl, q.bl, v), lerp(q.cT, q.cB, v), lerp(q.tr, q.br, v), u);
  }
  /* the same surface cut into n tiles ACROSS u, each with its own fill — a zoned
     ground plate. Tile edges are true sub-curves of the outline (quadratic de
     Casteljau on [a,b]), so no seam drifts off the plane. Fills go down first, then
     the hairline seams, then the 1.25 contour on top: painting the outline first lets
     the fills eat it, which is how soil() and calGrid() are ordered too. */
  function zonedSurface(x, y, w, h, fills) {
    var q = srfGeom(x, y, w, h), n = fills.length, out = [], i, a, b;
    function sub(p0, c, p1, t0, t1) { return lerp(lerp(p0, c, t0), lerp(c, p1, t0), t1); }
    for (i = 0; i < n; i++) {
      a = i / n; b = (i + 1) / n;
      out.push(P('M' + pt(qAt(q.tl, q.cT, q.tr, a)) +
        'Q' + pt(sub(q.tl, q.cT, q.tr, a, b)) + ' ' + pt(qAt(q.tl, q.cT, q.tr, b)) +
        'L' + pt(qAt(q.bl, q.cB, q.br, b)) +
        'Q' + pt(sub(q.bl, q.cB, q.br, a, b)) + ' ' + pt(qAt(q.bl, q.cB, q.br, a)) + 'Z',
        { fill: fills[i], stroke: false }));
    }
    for (i = 1; i < n; i++) {
      a = i / n;
      out.push(line(qAt(q.tl, q.cT, q.tr, a)[0], qAt(q.tl, q.cT, q.tr, a)[1],
        qAt(q.bl, q.cB, q.br, a)[0], qAt(q.bl, q.cB, q.br, a)[1], { sw: HAIR }));
    }
    out.push(surface(x, y, w, h, 'none'));
    return g(out);
  }
  /* real gear: n teeth cut from two radii */
  function gear(cx, cy, r, n, f) {
    n = n || 8;
    var ri = r * 0.68, tw = 0.3, d = '', i, a0, a1, b0, b1, step = Math.PI * 2 / n;
    for (i = 0; i < n; i++) {
      a0 = i * step - step * tw; a1 = i * step + step * tw;
      b0 = i * step + step * (0.5 - tw * 0.6); b1 = (i + 1) * step - step * (0.5 - tw * 0.6);
      d += (i ? 'L' : 'M') + (cx + Math.cos(a0) * r) + ' ' + (cy + Math.sin(a0) * r);
      d += 'A' + r + ' ' + r + ' 0 0 1 ' + (cx + Math.cos(a1) * r) + ' ' + (cy + Math.sin(a1) * r);
      d += 'L' + (cx + Math.cos(b0) * ri) + ' ' + (cy + Math.sin(b0) * ri);
      d += 'A' + ri + ' ' + ri + ' 0 0 1 ' + (cx + Math.cos(b1) * ri) + ' ' + (cy + Math.sin(b1) * ri);
    }
    return g([P(d + 'Z', { fill: f, sw: HAIR }), circle(cx, cy, r * 0.34, { fill: '#fff', sw: HAIR })]);
  }
  /* eye: two mirrored arcs + pupil */
  function eye(cx, cy, w, h, f, pf) {
    return g([
      P('M' + (cx - w) + ' ' + cy + 'Q' + cx + ' ' + (cy - h * 2) + ' ' + (cx + w) + ' ' + cy +
        'Q' + cx + ' ' + (cy + h * 2) + ' ' + (cx - w) + ' ' + cy + 'Z', { fill: f }),
      circle(cx, cy, h * 0.5, { fill: pf || INK, stroke: false })
    ]);
  }
  function sun(cx, cy, r, c, rays) {
    var out = [circle(cx, cy, r, { fill: c })], i, a;
    for (i = 0; i < (rays || 8); i++) {
      a = i * Math.PI * 2 / (rays || 8);
      out.push(line(cx + Math.cos(a) * (r + 1.4), cy + Math.sin(a) * (r + 1.4),
        cx + Math.cos(a) * (r + 3), cy + Math.sin(a) * (r + 3), {}));
    }
    return g(out);
  }
  /* arrow with a real head; curve>0 bows the shaft */
  function arrow(x1, y1, x2, y2, curve, c) {
    var mx = (x1 + x2) / 2, my = (y1 + y2) / 2, dx = x2 - x1, dy = y2 - y1,
      L = Math.sqrt(dx * dx + dy * dy) || 1, nx = -dy / L, ny = dx / L,
      cxp = mx + nx * (curve || 0), cyp = my + ny * (curve || 0),
      ang = Math.atan2(y2 - cyp, x2 - cxp), hl = 2.1, hw = 0.62;
    return g([
      P('M' + x1 + ' ' + y1 + 'Q' + cxp + ' ' + cyp + ' ' + x2 + ' ' + y2, { stroke: c || INK }),
      poly([[x2, y2],
        [x2 - Math.cos(ang - hw) * hl, y2 - Math.sin(ang - hw) * hl],
        [x2 - Math.cos(ang + hw) * hl, y2 - Math.sin(ang + hw) * hl]], { fill: c || INK, stroke: c || INK })
    ]);
  }
  /* uniform profile: rail + equal arrows */
  function profile(x, y0, y1, lens, c) {
    var out = [line(x, y0, x, y1, {})], n = lens.length, i, yy;
    for (i = 0; i < n; i++) {
      yy = y0 + (y1 - y0) * (i + 0.5) / n;
      out.push(arrow(x + 0.9, yy, x + 0.9 + lens[i], yy, 0, c));
    }
    return g(out);
  }
  /* the real thing: a log-law wind profile over a rough ground plane.
     u(z) = ln(z/z0) / ln(H/z0) — arrow lengths ARE the log law, and the envelope
     curve through their tips is what makes it read as an ABL and not a bar chart. */
  function abl(x, ground, top, maxLen, c) {
    var n = 5, out = [line(x, top, x, ground, {})], tips = [], i, hf, yy, z, u, len, sp;
    for (i = 0; i < n; i++) {
      hf = i / (n - 1);
      yy = (ground - 1.8) - hf * ((ground - 1.8) - top);
      z = 0.06 + hf * 0.94;
      u = Math.log(z / 0.02) / Math.log(50);
      len = maxLen * u;
      out.push(arrow(x + 0.7, yy, x + 0.7 + len, yy, 0, c));
      tips.push([x + 0.7 + len, yy]);
    }
    out.push(P(smooth(tips), { stroke: c, sw: HAIR }));
    out.push(line(x - 0.8, ground, x + maxLen + 2.2, ground, {}));
    sp = maxLen / 4.6;
    for (i = 0; i < 5; i++)
      out.push(line(x + 0.6 + i * sp, ground, x - 0.9 + i * sp, ground + 1.9, { stroke: '#8f8f8f', sw: HAIR }));
    return g(out);
  }
  function dots(x, y, cols, rows, step, r, c) {
    var out = [], i, j;
    for (j = 0; j < rows; j++) for (i = 0; i < cols; i++)
      out.push(circle(x + i * step, y + j * step, r, { fill: c, stroke: false }));
    return g(out);
  }
  function grid(x, y, w, h, n, c) {
    var out = [], i;
    for (i = 0; i <= n; i++) {
      out.push(line(x + w * i / n, y, x + w * i / n, y + h, { stroke: c, sw: HAIR }));
      out.push(line(x, y + h * i / n, x + w, y + h * i / n, { stroke: c, sw: HAIR }));
    }
    return g(out);
  }
  /* terrain: a FACETED ridge. Rolling cubic hills read as a children's landscape;
     terrain in a CFD case is a triangulated surface, so the silhouette is straight
     segments of uneven length and pitch. */
  function terrain(x, y, w, h, f) {
    var p = [[0, .62], [.15, .27], [.28, .5], [.45, .09], [.6, .38], [.79, .13], [1, .46]],
      d = 'M' + x.toFixed(2) + ' ' + (y + h * p[0][1]).toFixed(2), i;
    for (i = 1; i < p.length; i++) d += 'L' + (x + w * p[i][0]).toFixed(2) + ' ' + (y + h * p[i][1]).toFixed(2);
    return P(d + 'L' + (x + w).toFixed(2) + ' ' + (y + h).toFixed(2) +
      'L' + x.toFixed(2) + ' ' + (y + h).toFixed(2) + 'Z', { fill: f });
  }
  /* broadleaf. Hand-set lobed silhouette — three uneven bumps and two notches. A
     parametric dome always converges on a circle, which reads as a lollipop.
     w scales the canopy; strokes are NOT scaled, so the two weights survive. */
  /* broadleaf. Five broad, LOW-amplitude inflections: enough asymmetry that it is
     not a lollipop, not so much that it becomes a cartoon cloud. The trunk flares
     at the base rather than being a stuck-on rectangle. */
  /* broadleaf. A SCALLOPED crown — seven lobes with real concave cusps between them.
     Any smooth closed curve, however asymmetric, reads as a mushroom cap at 24px; it
     is the notches that say foliage. Trunk is thin and runs a third of the height. */
  function tree(cx, base, w, f, tf) {
    var n = 7, R = w, oy = base - w * 1.9, rc = R * 0.76,
      mag = [1, 0.93, 1, 0.89, 0.98, 0.91, 0.96], step = Math.PI * 2 / n, i, d;
    function pR(r, t) { return (cx + Math.cos(t) * r).toFixed(2) + ' ' + (oy + Math.sin(t) * r).toFixed(2); }
    function cusp(k) { return (k - 0.5) * step - Math.PI / 2; }
    d = 'M' + pR(rc, cusp(0));
    for (i = 0; i < n; i++)
      d += 'Q' + pR(R * 1.22 * mag[i], i * step - Math.PI / 2) + ' ' + pR(rc, cusp(i + 1));
    var tw = w * 0.16, tt = w * 0.11, th = base - oy;
    var trunk = 'M' + (cx - tw).toFixed(2) + ' ' + base.toFixed(2) +
      'C' + (cx - tw * 0.8).toFixed(2) + ' ' + (base - th * 0.42).toFixed(2) +
      ' ' + (cx - tt).toFixed(2) + ' ' + (base - th * 0.6).toFixed(2) +
      ' ' + (cx - tt).toFixed(2) + ' ' + oy.toFixed(2) +
      'L' + (cx + tt).toFixed(2) + ' ' + oy.toFixed(2) +
      'C' + (cx + tt).toFixed(2) + ' ' + (base - th * 0.6).toFixed(2) +
      ' ' + (cx + tw * 0.8).toFixed(2) + ' ' + (base - th * 0.42).toFixed(2) +
      ' ' + (cx + tw).toFixed(2) + ' ' + base.toFixed(2) + 'Z';
    return g([P(trunk, { fill: tf, tint: true }), P(d + 'Z', { fill: f })]);
  }
  function bldgs(x, base, f) {
    return g([
      rect(x, base - 5.5, 4, 5.5, 0.4, { fill: f }),
      rect(x + 4.6, base - 8.5, 4, 8.5, 0.4, { fill: f }),
      rect(x + 9.2, base - 3.8, 4, 3.8, 0.4, { fill: f })
    ]);
  }
  /* buildings standing INSIDE a cylinder domain. Two rules make it read as enclosed:
     each base sits on the floor ellipse (not on a shared flat line), and the cluster is
     inset well within the radius so the silhouette never touches the tube wall. */
  function bldgsInCyl(cx, floorY, rx, f) {
    var ry = rx * 0.34, spec = [[-3.5, 2.9, 4.6], [0, 3.1, 6.6], [3.5, 2.7, 3.8]],
      out = [], i, dx, base;
    for (i = 0; i < spec.length; i++) {
      dx = spec[i][0];
      base = floorY + ry * Math.sqrt(Math.max(0, 1 - Math.pow((dx + spec[i][1] * 0.5) / rx, 2)));
      out.push(rect(cx + dx - spec[i][1] / 2, base - spec[i][2], spec[i][1], spec[i][2], 0.4, { fill: f }));
    }
    return g(out);
  }
  function thermo(cx, top, h, f, fl) {
    var bulb = 2.4, sh = h - bulb * 1.6, gw = 1.6;
    return g([
      P('M' + (cx - gw) + ' ' + (top + sh) + 'L' + (cx - gw) + ' ' + (top + gw) +
        'A' + gw + ' ' + gw + ' 0 0 1 ' + (cx + gw) + ' ' + (top + gw) + 'L' + (cx + gw) + ' ' + (top + sh) + 'Z', { fill: f }),
      circle(cx, top + h - bulb, bulb, { fill: f }),
      rect(cx - 0.6, top + sh * 0.4, 1.2, sh * 0.62, 0.6, { fill: fl, tint: true }),
      circle(cx, top + h - bulb, bulb * 0.52, { fill: fl, tint: true })
    ]);
  }
  function flame(cx, base, h, f, core) {
    var w = h * 0.42;
    return g([
      P('M' + cx + ' ' + (base - h) +
        'C' + (cx + w) + ' ' + (base - h * 0.62) + ' ' + (cx + w * 1.05) + ' ' + (base - h * 0.28) + ' ' + cx + ' ' + base +
        'C' + (cx - w * 1.05) + ' ' + (base - h * 0.28) + ' ' + (cx - w) + ' ' + (base - h * 0.62) + ' ' + cx + ' ' + (base - h) +
        'Z', { fill: f }),
      P('M' + cx + ' ' + (base - h * 0.55) +
        'C' + (cx + w * 0.5) + ' ' + (base - h * 0.32) + ' ' + (cx + w * 0.5) + ' ' + (base - h * 0.12) + ' ' + cx + ' ' + (base - h * 0.04) +
        'C' + (cx - w * 0.5) + ' ' + (base - h * 0.12) + ' ' + (cx - w * 0.5) + ' ' + (base - h * 0.32) + ' ' + cx + ' ' + (base - h * 0.55) +
        'Z', { fill: core, stroke: false })
    ]);
  }
  /* standing figure. The previous one was a head over a tapered slab, which at 24px
     read as a bowling pin. This is a pictogram: sloped shoulders wider than the hips,
     a clear gap under the head, and a notch splitting two legs. */
  function person(cx, top, h, f) {
    var hr = h * 0.135, shY = top + h * 0.42, sw = h * 0.205, hipY = top + h * 0.6,
      hw = h * 0.145, legTop = top + h * 0.76, bot = top + h, nt = h * 0.04,
      N = function (v) { return v.toFixed(2); };
    var d = 'M' + N(cx - sw) + ' ' + N(shY) +
      'Q' + N(cx - sw - h * 0.015) + ' ' + N(shY + (hipY - shY) * 0.6) + ' ' + N(cx - hw) + ' ' + N(hipY) +
      'L' + N(cx - hw) + ' ' + N(bot) +
      'L' + N(cx - nt) + ' ' + N(bot) +
      'L' + N(cx - nt) + ' ' + N(legTop) +
      'L' + N(cx + nt) + ' ' + N(legTop) +
      'L' + N(cx + nt) + ' ' + N(bot) +
      'L' + N(cx + hw) + ' ' + N(bot) +
      'Q' + N(cx + sw + h * 0.015) + ' ' + N(shY + (hipY - shY) * 0.6) + ' ' + N(cx + sw) + ' ' + N(shY) +
      'Q' + N(cx) + ' ' + N(shY - h * 0.035) + ' ' + N(cx - sw) + ' ' + N(shY) + 'Z';
    return g([circle(cx, top + hr, hr, { fill: f }), P(d, { fill: f })]);
  }
  function doc(x, y, w, h, f, fold) {
    var c = w * 0.32;
    return g([
      P('M' + x + ' ' + y + 'L' + (x + w - c) + ' ' + y + 'L' + (x + w) + ' ' + (y + c) +
        'L' + (x + w) + ' ' + (y + h) + 'L' + x + ' ' + (y + h) + 'Z', { fill: f }),
      P('M' + (x + w - c) + ' ' + y + 'L' + (x + w - c) + ' ' + (y + c) + 'L' + (x + w) + ' ' + (y + c), { fill: fold })
    ]);
  }
  /* wind rose: petals are TINTED — stroked in their own fill. A 1.25 ink contour on
     both sides is wider than the short petals themselves, so an outline here would
     read as a black crucifix. The reference circle carries the contour instead, and
     magnitude is carried by length plus a light/base value split — near-equal petals
     in one colour read as a fan, not as measured data. */
  /* wind rose. Filled wedges CANNOT work here: eight of them at 24px, tinted (so each
     grows ~0.6 units per side), merge into four broad blades — i.e. into this set's own
     fan motif. Radial spokes cannot: a fan needs broad curved blades, so thin graded
     spokes inside a reference circle read as a rose diagram and nothing else. */
  function rose(cx, cy, r, mags, cHi, cLo) {
    var out = [circle(cx, cy, r, { fill: '#fff' })], n = mags.length,
      i, a, r0 = 1.1, span = r - 3.4, L, f;
    for (i = 0; i < n; i++) {
      a = i * Math.PI * 2 / n - Math.PI / 2;
      L = r0 + span * mags[i];
      f = mags[i] >= 0.55 ? cHi : cLo;
      out.push(line(cx + Math.cos(a) * r0, cy + Math.sin(a) * r0,
        cx + Math.cos(a) * L, cy + Math.sin(a) * L, { stroke: f }));
    }
    out.push(circle(cx, cy, r, { fill: 'none' }));
    return g(out);
  }
  function compass(cx, cy, r, c) {
    return g([
      circle(cx, cy, r, { fill: '#fff' }),
      poly([[cx, cy - r * 0.72], [cx + r * 0.3, cy], [cx, cy + r * 0.72], [cx - r * 0.3, cy]], { fill: c }),
      line(cx - r * 0.26, cy, cx + r * 0.26, cy, { sw: HAIR })
    ]);
  }
  function nodes(x, y, w, h, c) {
    var A = [[x, y], [x, y + h / 2], [x, y + h]], B = [[x + w / 2, y + h * 0.25], [x + w / 2, y + h * 0.75]],
      C = [[x + w, y + h / 2]], out = [], i, j;
    for (i = 0; i < 3; i++) for (j = 0; j < 2; j++) out.push(line(A[i][0], A[i][1], B[j][0], B[j][1], { stroke: '#8f8f8f', sw: HAIR }));
    for (j = 0; j < 2; j++) out.push(line(B[j][0], B[j][1], C[0][0], C[0][1], { stroke: '#8f8f8f', sw: HAIR }));
    [].concat(A, B, C).forEach(function (n) { out.push(circle(n[0], n[1], 1.5, { fill: c })); });
    return g(out);
  }
  function plot(x, y, w, h, c) {
    return g([
      P('M' + x + ' ' + y + 'L' + x + ' ' + (y + h) + 'L' + (x + w) + ' ' + (y + h), {}),
      P('M' + (x + 1) + ' ' + (y + h * 0.85) +
        'C' + (x + w * 0.3) + ' ' + (y + h * 0.2) + ' ' + (x + w * 0.45) + ' ' + (y + h * 0.9) + ' ' + (x + w * 0.62) + ' ' + (y + h * 0.45) +
        'S' + (x + w * 0.85) + ' ' + (y + h * 0.1) + ' ' + (x + w) + ' ' + (y + h * 0.16), { stroke: c })
    ]);
  }
  /* probe points sampled ON a surface — rows follow the plane's own curvature, so the
     grid sits on the geometry instead of floating over it as a rectangular lattice. */
  function srfDots(x, y, w, h, nu, nv, r, c) {
    var q = srfGeom(x, y, w, h), out = [], i, j, tu, tv, a, b, cc, p;
    for (j = 0; j < nv; j++) {
      tv = (j + 0.5) / nv;
      a = lerp(q.tl, q.bl, tv); cc = lerp(q.cT, q.cB, tv); b = lerp(q.tr, q.br, tv);
      for (i = 0; i < nu; i++) {
        tu = (i + 0.5) / nu;
        p = qAt(a, cc, b, tu);
        out.push(circle(+p[0].toFixed(2), +p[1].toFixed(2), r, { fill: c, stroke: false }));
      }
    }
    return g(out);
  }
  /* calendar. A real month grid: header band, two binder rings, and a cols x rows day
     lattice — so a highlighted span lands on actual date squares instead of on a blank
     card. `sel` = [startCol, endCol, row]. The highlight is painted OVER the hairlines,
     with its own contour, or the grey lattice crosses it and the run reads as noise. */
  function calGrid(x, y, w, h, c, cols, rows, selC, sel) {
    var hb = h * 0.3, gx = x + w * 0.055, gw = w * 0.89, gy = y + hb + h * 0.055,
      gh = h - hb - h * 0.11, cw = gw / cols, ch = gh / rows, out = [], i, j;
    out.push(rect(x, y, w, h, 1.2, { fill: '#fff' }));
    out.push(rect(x, y, w, hb, 1.2, { fill: c, stroke: false }));
    out.push(P('M' + x.toFixed(2) + ' ' + (y + hb).toFixed(2) + 'L' + (x + w).toFixed(2) + ' ' + (y + hb).toFixed(2), {}));
    for (i = 1; i < cols; i++) out.push(line(gx + i * cw, gy, gx + i * cw, gy + gh, { stroke: '#8f8f8f', sw: HAIR }));
    for (j = 1; j < rows; j++) out.push(line(gx, gy + j * ch, gx + gw, gy + j * ch, { stroke: '#8f8f8f', sw: HAIR }));
    if (sel) out.push(rect(gx + sel[0] * cw, gy + sel[2] * ch, (sel[1] - sel[0] + 1) * cw, ch, 0.3, { fill: selC }));
    out.push(rect(x, y, w, h, 1.2, { fill: 'none' }));
    out.push(line(x + w * 0.28, y - 1.7, x + w * 0.28, y + hb * 0.5, {}));
    out.push(line(x + w * 0.72, y - 1.7, x + w * 0.72, y + hb * 0.5, {}));
    return g(out);
  }
  function cal(x, y, w, h, c) {
    /* superseded by calGrid — a blank card with no day cells. Kept only so an older
       page or export script cannot break; nothing in the shipped set references it. */
    var out = [rect(x, y, w, h, 1.2, { fill: '#fff' }),
      P('M' + x + ' ' + (y + h * 0.3) + 'L' + (x + w) + ' ' + (y + h * 0.3), { sw: HAIR }),
      rect(x, y, w, h * 0.3, 1.2, { fill: c, stroke: false }),
      rect(x, y, w, h, 1.2, { fill: 'none' }),
      line(x + w * 0.25, y - 1.6, x + w * 0.25, y + 1, {}),
      line(x + w * 0.75, y - 1.6, x + w * 0.75, y + 1, {})];
    return g(out);
  }
  function chip(x, y, s, c) {
    var out = [rect(x, y, s, s, 1.4, { fill: '#fff' }), rect(x + s * 0.26, y + s * 0.26, s * 0.48, s * 0.48, 0.6, { fill: c })], i, p;
    for (i = 0; i < 3; i++) {
      p = x + s * (0.25 + i * 0.25);
      out.push(line(p, y, p, y - 1.8, { sw: HAIR }));
      out.push(line(p, y + s, p, y + s + 1.8, { sw: HAIR }));
      out.push(line(x, p - x + y, x - 1.8, p - x + y, { sw: HAIR }));
      out.push(line(x + s, p - x + y, x + s + 1.8, p - x + y, { sw: HAIR }));
    }
    return g(out);
  }
  function drop(cx, cy, r, f) {
    return P('M' + cx + ' ' + (cy - r * 1.5) +
      'C' + (cx + r * 0.9) + ' ' + (cy - r * 0.35) + ' ' + (cx + r) + ' ' + (cy + r * 0.1) + ' ' + cx + ' ' + (cy + r) +
      'C' + (cx - r) + ' ' + (cy + r * 0.1) + ' ' + (cx - r * 0.9) + ' ' + (cy - r * 0.35) + ' ' + cx + ' ' + (cy - r * 1.5) +
      'Z', { fill: f });
  }
  function cloud(cx, cy, r, f) {
    return P('M' + (cx - r * 1.5) + ' ' + (cy + r * 0.55) +
      'A' + (r * 0.72) + ' ' + (r * 0.72) + ' 0 0 1 ' + (cx - r * 0.85) + ' ' + (cy - r * 0.3) +
      'A' + (r * 0.95) + ' ' + (r * 0.95) + ' 0 0 1 ' + (cx + r * 0.55) + ' ' + (cy - r * 0.42) +
      'A' + (r * 0.7) + ' ' + (r * 0.7) + ' 0 0 1 ' + (cx + r * 1.45) + ' ' + (cy + r * 0.55) +
      'Z', { fill: f });
  }
  function dome(cx, base, r, f) {
    return g([
      P('M' + (cx - r) + ' ' + base + 'A' + r + ' ' + r + ' 0 0 1 ' + (cx + r) + ' ' + base + 'Z', { fill: f }),
      line(cx - r - 1, base, cx + r + 1, base, {})
    ]);
  }
  /* discrete value ramp — a legend's colour scale. The one genuinely new idea in the
     brief: no existing motif says "ordered categories", and faking it with bars or
     swatches would read as data or as a material, not as a key. */
  function ramp(x, y, w, h, cols) {
    var n = cols.length, ch = h / n, out = [], i;
    for (i = 0; i < n; i++)
      out.push(rect(x, y + i * ch, w, ch, 0, { fill: cols[i], stroke: false }));
    for (i = 1; i < n; i++)
      out.push(line(x, y + i * ch, x + w, y + i * ch, { sw: HAIR }));
    out.push(rect(x, y, w, h, 0.6, { fill: 'none' }));
    return g(out);
  }

  /* ---------- indoor species / comfort motifs ----------
     Four considered additions, not one-offs: nothing in the library said "meshed
     occupant", "core + skin", "reclining body" or "opening in a wall", and faking
     them from person/room would have collided with Thermal_Comfort and Indoor_Case. */

  /* the meshed occupant. Every edge is straight — that is the point: it tells the user
     this is an LOD-0 block body, not anatomy. The MOUTH is an accent patch inset into the
     facing side of the head (the mouth being its own surface is what distinguishes this
     component), and plumeC adds ONE exhaled puff — a second, smaller puff is sub-pixel
     at 24 and merges into the first. */
  function manikin(cx, top, h, f, plumeC) {
    var hs = h * 0.22, neck = h * 0.05, shY = top + hs + neck,
      tw = h * 0.36, tBot = top + h * 0.66, legW = h * 0.135, gap = h * 0.07,
      bot = top + h, out = [];
    out.push(rect(cx - hs / 2, top, hs, hs, 0.3, { fill: f }));
    out.push(rect(cx - tw / 2, shY, tw, tBot - shY, 0.3, { fill: f }));
    out.push(rect(cx - gap / 2 - legW, tBot, legW, bot - tBot, 0.3, { fill: f }));
    out.push(rect(cx + gap / 2, tBot, legW, bot - tBot, 0.3, { fill: f }));
    if (plumeC) {
      out.push(rect(cx + hs * 0.12, top + hs * 0.55, hs * 0.3, hs * 0.32, 0.15,
        { fill: plumeC, tint: true }));
      out.push(circle(cx + hs * 1.45, top + hs * 0.72, h * 0.085, { fill: plumeC, tint: true }));
    }
    return g(out);
  }

  /* the Gagge two-node body: a skin capsule with a concentric core capsule inside it.
     The nesting IS the model's identity, and it is what keeps this clear of the plain
     person used by Thermal_Comfort. */
  function twoNode(cx, top, h, f) {
    var hr = h * 0.13, shY = top + h * 0.32, bot = top + h, bw = h * 0.42,
      cw = bw * 0.44, inset = h * 0.11;
    return g([
      circle(cx, top + hr, hr, { fill: '#fff' }),
      rect(cx - bw / 2, shY, bw, bot - shY, bw * 0.42, { fill: '#fff' }),
      rect(cx - cw / 2, shY + inset, cw, bot - shY - inset * 2, cw * 0.45,
        { fill: f.base, tint: true })
    ]);
  }

  /* reclining body under a quilt, on a mattress slab. The mound plus the turned-back
     fold is what says sleep without a moon or a clock — Comfort_Hours already owns the
     clock badge on the neighbouring tab. */
  function sleeper(cx, base, w, f, qc) {
    var x0 = cx - w / 2, x1 = cx + w / 2, md = w * 0.16, a = x0 + w * 0.24,
      mh = w * 0.23, hr = w * 0.125, N = function (v) { return v.toFixed(2); };
    return g([
      rect(x0, base, w, md, 0.6, { fill: '#fff' }),
      circle(x0 + w * 0.13, base - hr * 1.45, hr, { fill: f }),
      P('M' + N(a) + ' ' + N(base) +
        'C' + N(a) + ' ' + N(base - mh * 1.3) + ' ' + N(a + w * 0.2) + ' ' + N(base - mh) +
        ' ' + N(a + w * 0.35) + ' ' + N(base - mh * 0.92) +
        'L' + N(x1 - w * 0.08) + ' ' + N(base - mh * 0.7) +
        'Q' + N(x1) + ' ' + N(base - mh * 0.6) + ' ' + N(x1) + ' ' + N(base) + 'Z',
        { fill: qc || f }),
      line(a + w * 0.09, base - mh * 0.72, a + w * 0.09, base - w * 0.02, { sw: HAIR })
    ]);
  }

  /* an opening in a wall: framed, with a glazed upper pane over a rail and a CLEAR lower
     pane, so flow can be drawn THROUGH it — which is what separates this from
     Indoor_Inlet/Outlet, where arrows cross a room's boundary instead. The pane gets a
     mullion, not a glint: a diagonal highlight makes the whole thing read as a screen. */
  function sash(x, y, w, h, c) {
    var rail = y + h * 0.44, gx = x + w * 0.12, gw = w * 0.76,
      gy = y + h * 0.09, gh = rail - y - h * 0.18;
    return g([
      rect(x, y, w, h, 0.8, { fill: '#fff' }),
      rect(gx, gy, gw, gh, 0.4, { fill: c }),
      line(gx + gw / 2, gy, gx + gw / 2, gy + gh, { sw: HAIR }),
      line(x, rail, x + w, rail, {})
    ]);
  }

  function badge(children) { return g(children); }
  function check(cx, cy, r) {
    return g([circle(cx, cy, r, { fill: '#2e8b3f', stroke: '#fff' }),
      P('M' + (cx - r * 0.42) + ' ' + cy + 'L' + (cx - r * 0.08) + ' ' + (cy + r * 0.38) + 'L' + (cx + r * 0.45) + ' ' + (cy - r * 0.35), { stroke: '#fff' })]);
  }
  function warn(cx, cy, r) {
    return g([P('M' + cx + ' ' + (cy - r) + 'L' + (cx + r * 0.95) + ' ' + (cy + r * 0.72) + 'L' + (cx - r * 0.95) + ' ' + (cy + r * 0.72) + 'Z', { fill: '#e8b31d' }),
      line(cx, cy - r * 0.28, cx, cy + r * 0.2, {}),
      circle(cx, cy + r * 0.5, 0.35, { fill: INK, stroke: false })]);
  }
  function clock(cx, cy, r) {
    return g([circle(cx, cy, r, { fill: '#fff' }),
      P('M' + cx + ' ' + (cy - r * 0.55) + 'L' + cx + ' ' + cy + 'L' + (cx + r * 0.45) + ' ' + (cy + r * 0.25), {})]);
  }
  function spark(cx, cy, r, c) {
    return g([line(cx - r, cy, cx + r, cy, { stroke: c }),
      line(cx, cy - r, cx, cy + r, { stroke: c })]);
  }
  function play(cx, cy, r, f) {
    return poly([[cx - r * 0.62, cy - r], [cx + r * 0.88, cy], [cx - r * 0.62, cy + r]], { fill: f });
  }

  /* ---------- second wave of motifs ---------- */

  /* open-topped iso volume — the indoor room. Top face carries the accent. */
  function room(cx, cy, r, h, f) {
    return isoBox(cx, cy, r, h, { top: f.lite, left: '#ffffff', right: '#dfe3e7' });
  }
  function fan(cx, cy, r, c) {
    var out = [], i, a;
    for (i = 0; i < 4; i++) {
      a = i * Math.PI / 2;
      out.push(P('M' + cx + ' ' + cy +
        'Q' + (cx + Math.cos(a - 0.5) * r * 1.15) + ' ' + (cy + Math.sin(a - 0.5) * r * 1.15) + ' ' +
        (cx + Math.cos(a) * r) + ' ' + (cy + Math.sin(a) * r) +
        'Q' + (cx + Math.cos(a + 0.42) * r * 0.72) + ' ' + (cy + Math.sin(a + 0.42) * r * 0.72) + ' ' + cx + ' ' + cy +
        'Z', { fill: c, tint: true }));
    }
    out.push(circle(cx, cy, r * 0.2, { fill: '#fff' }));
    return g(out);
  }
  function grille(x, y, w, h, c) {
    var out = [rect(x, y, w, h, 0.8, { fill: '#fff' })], i;
    for (i = 1; i < 4; i++) out.push(line(x + w * i / 4, y + 0.9, x + w * i / 4, y + h - 0.9, { stroke: c, sw: HAIR }));
    return g(out);
  }
  function virus(cx, cy, r, c) {
    var out = [circle(cx, cy, r, { fill: c, tint: true })], i, a;
    for (i = 0; i < 6; i++) {
      a = i * Math.PI / 3;
      out.push(line(cx + Math.cos(a) * r, cy + Math.sin(a) * r,
        cx + Math.cos(a) * (r + 1.5), cy + Math.sin(a) * (r + 1.5), { stroke: c, sw: HAIR }));
    }
    return g(out);
  }
  function swatch(x, y, s, c) {
    return g([rect(x, y, s, s, 0.9, { fill: '#fff' }), rect(x + s * 0.24, y + s * 0.24, s * 0.52, s * 0.52, 0.5, { fill: c, tint: true })]);
  }
  function sliders(x, y, w, gap, c) {
    var out = [], i, at = [0.62, 0.3, 0.78];
    for (i = 0; i < 3; i++) {
      out.push(line(x, y + i * gap, x + w, y + i * gap, { stroke: '#8f8f8f', sw: HAIR }));
      out.push(circle(x + w * at[i], y + i * gap, 1.5, { fill: c }));
    }
    return g(out);
  }
  /* speech bubble — a conversation with the assistant. ONE closed contour: body and
     tail are a single path, because a triangle laid over a rounded rect would draw an
     ink line across the junction where the two meet and read as a flag on a box. The
     tail leaves the bottom edge left-of-centre so the top-left stays clear for the
     spark badge, which is where this set puts sparkles. */
  function bubble(x, y, w, h, r, f) {
    var N = function (v) { return v.toFixed(2); };
    var bot = y + h, tx = x + w * 0.28, tw = w * 0.22, apex = [tx - 1.6, bot + 3.4];
    var d = 'M' + N(x + r) + ' ' + N(y) +
      'L' + N(x + w - r) + ' ' + N(y) +
      'A' + N(r) + ' ' + N(r) + ' 0 0 1 ' + N(x + w) + ' ' + N(y + r) +
      'L' + N(x + w) + ' ' + N(bot - r) +
      'A' + N(r) + ' ' + N(r) + ' 0 0 1 ' + N(x + w - r) + ' ' + N(bot) +
      'L' + N(tx + tw) + ' ' + N(bot) +
      'L' + N(apex[0]) + ' ' + N(apex[1]) +
      'L' + N(tx) + ' ' + N(bot) +
      'L' + N(x + r) + ' ' + N(bot) +
      'A' + N(r) + ' ' + N(r) + ' 0 0 1 ' + N(x) + ' ' + N(bot - r) +
      'L' + N(x) + ' ' + N(y + r) +
      'A' + N(r) + ' ' + N(r) + ' 0 0 1 ' + N(x + r) + ' ' + N(y) + 'Z';
    return P(d, { fill: f || '#ffffff' });
  }
  function pill(x, y, w, h, c, knobLeft) {
    var r = h / 2;
    return g([rect(x, y, w, h, r, { fill: c }),
      circle(knobLeft ? x + r : x + w - r, y + r, r * 0.62, { fill: '#fff' })]);
  }
  /* stacked disks — a dataset. ONE outer silhouette plus hairline front arcs for the
     interior seams. Drawing each disk as its own closed cylinder put a body stroke and
     a cap stroke about a unit apart on every seam, which merged into a thick band. */
  function dataset(x, y, w, n, gap, c) {
    var rx = w / 2, ry = w * 0.19, bot = y + (n - 1) * gap + gap * 0.72, out = [], i, cy;
    out.push(P('M' + x.toFixed(2) + ' ' + y.toFixed(2) +
      'L' + x.toFixed(2) + ' ' + bot.toFixed(2) +
      'A' + rx + ' ' + ry + ' 0 0 0 ' + (x + w).toFixed(2) + ' ' + bot.toFixed(2) +
      'L' + (x + w).toFixed(2) + ' ' + y.toFixed(2) + 'Z', { fill: '#fff' }));
    for (i = 1; i < n; i++) {
      cy = y + i * gap;
      out.push(P('M' + x.toFixed(2) + ' ' + cy.toFixed(2) +
        'A' + rx + ' ' + ry + ' 0 0 0 ' + (x + w).toFixed(2) + ' ' + cy.toFixed(2), { fill: 'none', sw: HAIR }));
    }
    out.push(ellipse(x + rx, y, rx, ry, { fill: c }));
    return g(out);
  }
  /* viewport / window frame */
  function frame(x, y, w, h, c) {
    return g([rect(x, y, w, h, 1.2, { fill: '#fff' }),
      rect(x, y, w, h * 0.24, 1.2, { fill: c, stroke: false }),
      rect(x, y, w, h, 1.2, { fill: 'none' }),
      line(x, y + h * 0.24, x + w, y + h * 0.24, { sw: HAIR })]);
  }
  /* nested model domains — the canonical WRF figure: concentric grids, each one finer than the
     one containing it, with the innermost the region actually being studied.

     Drawn as plain concentric rectangles rather than as three nested GRIDS, which is what this
     wanted to be. A nest is finer than its parent by construction, so an honest three-level grid
     puts the innermost rules about 2.3 units apart — under 2.5 px once Grasshopper rasterises it,
     where a 0.85 hairline turns to a grey wash and the glyph reads as a filled box with a border.
     The single cross on the outer domain is enough to say "grid"; depth is carried by nesting,
     which survives the raster. Only the innermost level takes the accent: it is the domain the
     component is about, and tinting all three flattens the hierarchy the motif exists to show. */
  function nest(x, y, w, h, f, levels) {
    levels = levels || 3;
    var out = [rect(x, y, w, h, 0.9, { fill: '#fff' })], i, dx, dy, iw, ih;

    /* Intermediate levels first, all white, so each one's contour is drawn over the fill of the
       level containing it. */
    for (i = 1; i < levels - 1; i++) {
      dx = w * 0.19 * i; dy = h * 0.19 * i;
      out.push(rect(x + dx, y + dy, w - 2 * dx, h - 2 * dy, 0.7, { fill: '#fff' }));
    }

    /* The grid rules go on AFTER the white levels and BEFORE the accent, spanning the full width.
       Drawing them first — the obvious order — puts them under every fill, so they survive only in
       the outermost ring, which at 24px is a couple of pixels of grey and reads as nothing at all:
       the glyph becomes three plain boxes and the "grid" half of the idea is lost. Crossing the
       intermediate contours is correct anyway; one grid runs through all the domains it covers. */
    if (levels > 1) {
      out.push(line(x + w / 2, y, x + w / 2, y + h, { stroke: '#8f8f8f', sw: HAIR }));
      out.push(line(x, y + h / 2, x + w, y + h / 2, { stroke: '#8f8f8f', sw: HAIR }));
    }

    if (levels > 1) {
      i = levels - 1;
      dx = w * 0.19 * i; dy = h * 0.19 * i;
      iw = w - 2 * dx; ih = h - 2 * dy;
      out.push(rect(x + dx, y + dy, iw, ih, 0.6,
        { fill: f.base, tint: Math.min(iw, ih) < 3.5 }));
    }

    out.push(rect(x, y, w, h, 0.9, { fill: 'none' }));
    return g(out);
  }
  /* stacked soil horizons */
  function soil(x, y, w, h, cols) {
    var out = [rect(x, y, w, h, 0.8, { fill: cols[0] })], i, n = cols.length;
    for (i = 1; i < n; i++) {
      out.push(P('M' + x + ' ' + (y + h * i / n) + 'L' + (x + w) + ' ' + (y + h * i / n), { sw: HAIR }));
      out.push(rect(x, y + h * i / n, w, h / n, 0, { fill: cols[i], stroke: false }));
    }
    out.push(rect(x, y, w, h, 0.8, { fill: 'none' }));
    for (i = 1; i < n; i++) out.push(line(x, y + h * i / n, x + w, y + h * i / n, { sw: HAIR }));
    return g(out);
  }
  /* graded bar cluster — binned directional or hourly data. Bars are only tinted when
     they are too narrow to carry an ink contour; above that they get the normal
     silhouette, or a wide histogram merges into one blob. */
  function bars(x, base, w, hs, cHi, cLo) {
    var out = [], n = hs.length, bw = w / (n * 1.45), i, hh, top = Math.max.apply(null, hs);
    for (i = 0; i < n; i++) {
      hh = hs[i];
      out.push(rect(x + i * bw * 1.45, base - hh, bw, hh, 0.3,
        { fill: hs[i] >= 0.55 * top ? cHi : cLo, tint: bw < 3 }));
    }
    out.push(line(x - 0.6, base, x + w, base, {}));
    return g(out);
  }
  /* probe rod with a conical tip */
  function probe(cx, top, bot, c) {
    return g([line(cx, top, cx, bot - 3.4, {}),
      poly([[cx - 1.7, bot - 3.8], [cx + 1.7, bot - 3.8], [cx, bot]], { fill: c })]);
  }
  function hatch(x, y, w, h, c) {
    var out = [], i, n = 5;
    for (i = 0; i <= n; i++) out.push(line(x + w * i / n, y, x + w * i / n - h * 0.5, y + h, { stroke: c, sw: HAIR }));
    return g(out);
  }
  /* refinement: outer grid with an inner denser box */
  function refine(x, y, w, h, c) {
    return g([rect(x, y, w, h, 0.8, { fill: '#fff' }), grid(x, y, w, h, 3, '#8f8f8f'),
      rect(x + w * 0.3, y + h * 0.3, w * 0.42, h * 0.42, 0.4, { fill: c }),
      grid(x + w * 0.3, y + h * 0.3, w * 0.42, h * 0.42, 2, '#ffffff')]);
  }

  /* ---------- badge slots: fixed positions, so meaning is positional ---------- */
  var BX = 17.0, BY = 17.0, BR = 4.4;
  var B = {
    gear: function (f) { return gear(BX, BY, BR, 8, f.base); },
    eye: function (f) { return eye(BX - 0.4, BY, 5.6, 2.9, '#fff'); },
    clock: function (f) { return clock(BX, BY, BR); },
    check: function (f) { return check(BX, BY, BR); },
    warn: function (f) { return warn(BX, BY, BR + 0.3); },
    out: function (f) { return arrow(12.4, BY + 1, 21.4, BY + 1, 0); },
    into: function (f) { return g([arrow(11.6, BY + 0.6, 19, BY + 0.6, 0), line(21.2, 13.4, 21.2, 21.4, {})]); },
    down: function (f) { return arrow(BX, 12.4, BX, 21.2, 0); },
    spark: function (f) { return g([spark(3.4, 3.4, 2.2, f.base), spark(7.6, 1.8, 1.4, f.base)]); }
  };


  /* ---------- structural motifs ----------
     The vocabulary the optimizer needs, added as motifs rather than drawn one-off per glyph
     (README, ADDING A COMPONENT). A member carries its SECTION DEPTH because depth is what the
     optimizer designs: I = b h^3 / 12, so a deeper member is a bigger I, in the icons exactly
     as in Visualize Result. */

  /* one beam or column segment, drawn with its section depth across the axis */
  function member(x1, y1, x2, y2, depth, fill) {
    var dx = x2 - x1, dy = y2 - y1, L = Math.sqrt(dx * dx + dy * dy) || 1,
      nx = -dy / L * depth / 2, ny = dx / L * depth / 2;
    return poly([[x1 + nx, y1 + ny], [x2 + nx, y2 + ny], [x2 - nx, y2 - ny], [x1 - nx, y1 - ny]],
      { fill: fill || '#ffffff' });
  }

  /* pin: the triangle sits UNDER the node it holds, apex on it, on a ground rule */
  function pinSupport(cx, cy, s, c) {
    return g([
      poly([[cx, cy], [cx - s * 0.62, cy + s], [cx + s * 0.62, cy + s]], { fill: c }),
      line(cx - s * 0.95, cy + s, cx + s * 0.95, cy + s, {})
    ]);
  }

  /* roller: the same triangle on wheels — free to slide, which is the whole distinction */
  function rollerSupport(cx, cy, s, c) {
    var yT = cy + s * 0.66, r = s * 0.3;
    return g([
      poly([[cx, cy], [cx - s * 0.62, yT], [cx + s * 0.62, yT]], { fill: c }),
      circle(cx - s * 0.36, yT + r, r, { fill: '#ffffff' }),
      circle(cx + s * 0.36, yT + r, r, { fill: '#ffffff' }),
      line(cx - s * 0.95, yT + r * 2, cx + s * 0.95, yT + r * 2, {})
    ]);
  }

  /* fixed: ground rule with the hatching that says no translation and no rotation */
  function fixedBase(cx, y, w, c) {
    var out = [line(cx - w / 2, y, cx + w / 2, y, {})], n = 4, i, x;
    for (i = 0; i < n; i++) {
      x = cx - w / 2 + (w * (i + 0.5)) / n;
      out.push(line(x, y, x - w * 0.14, y + w * 0.22, { stroke: c, sw: HAIR }));
    }
    return g(out);
  }

  /* uniformly distributed load: a rail with equal arrows onto the member below it */
  function udl(x0, x1, y, len, n, c) {
    var out = [line(x0, y, x1, y, { stroke: c })], i, x;
    for (i = 0; i < n; i++) {
      x = x0 + ((x1 - x0) * (i + 0.5)) / n;
      out.push(arrow(x, y + 0.5, x, y + len, 0, c));
    }
    return g(out);
  }

  /* bending moment: the sagging parabola, hung off the member's axis and filled */
  function bending(x, y, w, h, c) {
    return P('M' + x + ' ' + y + 'Q' + (x + w / 2) + ' ' + (y + h * 2) + ' ' + (x + w) + ' ' + y + 'Z',
      { fill: c, sw: HAIR });
  }

  var M = {
    isoBox: isoBox, isoGhost: isoGhost, cyl: cyl, surface: surface, gear: gear, eye: eye,
    sun: sun, arrow: arrow, profile: profile, dots: dots, grid: grid, terrain: terrain,
    tree: tree, bldgs: bldgs, thermo: thermo, flame: flame, person: person, doc: doc,
    rose: rose, compass: compass, nodes: nodes, plot: plot, chip: chip,
    drop: drop, cloud: cloud, dome: dome, check: check, warn: warn, clock: clock,
    spark: spark, play: play, badge: badge,
    room: room, fan: fan, grille: grille, virus: virus, swatch: swatch, sliders: sliders,
    pill: pill, bubble: bubble, dataset: dataset, frame: frame, nest: nest, soil: soil, bars: bars, probe: probe,
    hatch: hatch, refine: refine, abl: abl, meshSurface: meshSurface, bldgsInCyl: bldgsInCyl,
    srfDots: srfDots, calGrid: calGrid, ramp: ramp,
    manikin: manikin, twoNode: twoNode, sleeper: sleeper, sash: sash,
    smooth: smooth, srfGeom: srfGeom, srfPoint: srfPoint, zonedSurface: zonedSurface,
    P: P, circle: circle, ellipse: ellipse, rect: rect, line: line, poly: poly, g: g,
    member: member, pinSupport: pinSupport, rollerSupport: rollerSupport,
    fixedBase: fixedBase, udl: udl, bending: bending
  };

  function wrap(body, size, noFit) {
    if (!noFit) body = fit(body);
    return '<svg xmlns="http://www.w3.org/2000/svg" width="' + (size || 24) + '" height="' + (size || 24) +
      '" viewBox="0 0 24 24" fill="none" stroke-linecap="round" stroke-linejoin="round">' + body + '</svg>';
  }

  /* ---------- safe-area guard ----------
     The raster engine had fit(); without the same here, a stray path is silently
     cropped by the viewBox. bbox() walks the emitted markup command-aware and pads
     by half a stroke; fit() nudges the glyph in with a translate, which does NOT
     scale strokes, so the two weights survive. */
  var SAFE_MIN = 0.7, SAFE_MAX = 23.3;
  function nums(s) { return (s.match(/-?\d*\.?\d+(?:e-?\d+)?/g) || []).map(Number); }
  function pathPoints(d) {
    var pts = [], re = /([MLCQSTAHVZmlcqstahvz])([^MLCQSTAHVZmlcqstahvz]*)/g, m, cmd, v, i, cur = [0, 0];
    while ((m = re.exec(d))) {
      cmd = m[1].toUpperCase(); v = nums(m[2]);
      if (cmd === 'Z') continue;
      if (cmd === 'H') { for (i = 0; i < v.length; i++) { cur = [v[i], cur[1]]; pts.push(cur); } continue; }
      if (cmd === 'V') { for (i = 0; i < v.length; i++) { cur = [cur[0], v[i]]; pts.push(cur); } continue; }
      if (cmd === 'A') { for (i = 0; i + 6 < v.length; i += 7) { cur = [v[i + 5], v[i + 6]]; pts.push(cur); } continue; }
      for (i = 0; i + 1 < v.length; i += 2) { cur = [v[i], v[i + 1]]; pts.push(cur); }
    }
    return pts;
  }
  function bbox(body) {
    var b = [Infinity, Infinity, -Infinity, -Infinity], m, re, half;
    function add(x, y, pad) {
      pad = pad || 0;
      if (x - pad < b[0]) b[0] = x - pad;
      if (y - pad < b[1]) b[1] = y - pad;
      if (x + pad > b[2]) b[2] = x + pad;
      if (y + pad > b[3]) b[3] = y + pad;
    }
    re = /<path d="([^"]+)"([^>]*)>/g;
    while ((m = re.exec(body))) {
      half = /stroke-width="([\d.]+)"/.exec(m[2]);
      half = half ? +half[1] / 2 : 0;
      pathPoints(m[1]).forEach(function (p) { add(p[0], p[1], half); });
    }
    re = /<circle cx="([-\d.]+)" cy="([-\d.]+)" r="([\d.]+)"([^>]*)>/g;
    while ((m = re.exec(body))) {
      half = /stroke-width="([\d.]+)"/.exec(m[4]); half = half ? +half[1] / 2 : 0;
      add(+m[1], +m[2], +m[3] + half);
    }
    re = /<ellipse cx="([-\d.]+)" cy="([-\d.]+)" rx="([\d.]+)" ry="([\d.]+)"([^>]*)>/g;
    while ((m = re.exec(body))) {
      half = /stroke-width="([\d.]+)"/.exec(m[5]); half = half ? +half[1] / 2 : 0;
      add(+m[1] - +m[3] - half, +m[2] - +m[4] - half);
      add(+m[1] + +m[3] + half, +m[2] + +m[4] + half);
    }
    re = /<rect x="([-\d.]+)" y="([-\d.]+)" width="([\d.]+)" height="([\d.]+)"([^>]*)>/g;
    while ((m = re.exec(body))) {
      half = /stroke-width="([\d.]+)"/.exec(m[5]); half = half ? +half[1] / 2 : 0;
      add(+m[1] - half, +m[2] - half);
      add(+m[1] + +m[3] + half, +m[2] + +m[4] + half);
    }
    return b;
  }
  function fit(body) {
    var b = bbox(body);
    if (!isFinite(b[0])) return body;
    var span = SAFE_MAX - SAFE_MIN, dx = 0, dy = 0;
    if (b[2] - b[0] > span) dx = (SAFE_MIN + SAFE_MAX) / 2 - (b[0] + b[2]) / 2;
    else if (b[0] < SAFE_MIN) dx = SAFE_MIN - b[0];
    else if (b[2] > SAFE_MAX) dx = SAFE_MAX - b[2];
    if (b[3] - b[1] > span) dy = (SAFE_MIN + SAFE_MAX) / 2 - (b[1] + b[3]) / 2;
    else if (b[1] < SAFE_MIN) dy = SAFE_MIN - b[1];
    else if (b[3] > SAFE_MAX) dy = SAFE_MAX - b[3];
    if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) return body;
    return '<g transform="translate(' + dx.toFixed(2) + ' ' + dy.toFixed(2) + ')">' + body + '</g>';
  }
  /* what a glyph exceeds before fit() nudges it — null when it fits */
  function overflow(body) {
    var b = bbox(body), span = SAFE_MAX - SAFE_MIN, w = b[2] - b[0], h = b[3] - b[1];
    return (w > span || h > span) ? { w: +w.toFixed(2), h: +h.toFixed(2) } : null;
  }

  /* ONE path to a finished glyph body. Both deliverables — the standalone SVG and the
     sprite symbol — must go through this, or the safe-area guard applies to only one. */
  function glyph(d) { return fit(d.draw(FAM[d.fam] || FAM.struct)); }

  root.OpsVec = { SW: SW, HAIR: HAIR, INK: INK, FAM: FAM, M: M, B: B, wrap: wrap, glyph: glyph, bbox: bbox, fit: fit, overflow: overflow, SAFE: [SAFE_MIN, SAFE_MAX] };
})(typeof window !== 'undefined' ? window : globalThis);
