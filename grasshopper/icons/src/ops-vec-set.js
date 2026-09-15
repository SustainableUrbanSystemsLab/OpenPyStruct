/* OpenPyStruct icon set — the twelve components, composed from the motif library in ops-vec.js.

   Layout budget: art (geometry plus half a stroke) inside 0.7..23.3 of the 24-unit canvas,
   enforced by V.fit(). A glyph with a bottom-right badge keeps its primary motif in the top-left
   ~15 units so the pair reads as subject plus modifier.

   HUE MEANS PHYSICS, NOT RIBBON POSITION (README). Eight of these are Structure; the three
   surrogate components are Prediction, the same family as Eddy3D's ML tab, because a trained
   network is the same idea here as there; Engine is Tooling, because it is container plumbing
   and not structural mechanics at all. */
(function (root) {
  var V = root.OpsVec, M = V.M, B = V.B;
  var W = '#ffffff';

  /* The plugin's ribbon sub-tabs, in order. The family here is the tab's DEFAULT; a component
     whose physics differs passes an override as the fourth argument to def(). */
  var TABS = [
    ['1 | Model', 'struct'],
    ['2 | Loads', 'struct'],
    ['3 | Settings', 'struct'],
    ['4 | Run', 'struct'],
    ['5 | Results', 'struct'],
    ['00 | Plugin', 'struct']
  ];

  var S = {}, ORDER = [];
  function def(tab, name, draw, famOverride) {
    S[name] = { tab: tab, fam: famOverride || TABS[tab][1], draw: draw };
    ORDER.push(name);
  }

  /* ── 1 Model ─────────────────────────────────────────────────── */

  /* A span held at its ends: the member with a real section depth, a pin and a roller. The two
     support marks are what separate this from Frame Model, and they are the ones the component
     actually asks for. */
  def(0, 'Beam_Model', function (f) {
    return [
      M.member(2.6, 10.6, 21.4, 10.6, 3.4, W),
      M.pinSupport(5.6, 12.3, 2.7, f.base),
      M.rollerSupport(18.4, 12.3, 2.7, f.lite)
    ].join('');
  });

  /* A portal: two columns and a beam on fixed bases. Drawn as a frame rather than a grid of bays
     because the component takes ANY planar topology — the portal is the smallest thing that is
     unmistakably a frame and not a beam. */
  def(0, 'Frame_Model', function (f) {
    return [
      M.member(5.0, 17.4, 5.0, 6.8, 2.4, W),
      M.member(19.0, 17.4, 19.0, 6.8, 2.4, W),
      M.member(3.8, 6.8, 20.2, 6.8, 2.6, f.lite),
      M.fixedBase(5.0, 17.8, 5.4, f.base),
      M.fixedBase(19.0, 17.8, 5.4, f.base)
    ].join('');
  });

  /* ── 2 Loads ─────────────────────────────────────────────────── */

  /* The load rail with its equal arrows onto the member — a uniformly distributed load, plus the
     single heavier point load, which is the pair every load case in this plugin is built from. */
  def(1, 'Load_Case', function (f) {
    return [
      M.udl(3.4, 20.6, 4.4, 5.0, 5, f.base),
      M.arrow(12.0, 2.6, 12.0, 10.0, 0, f.dark),
      M.member(2.6, 13.4, 21.4, 13.4, 3.0, W),
      M.pinSupport(5.0, 14.9, 2.3, f.lite),
      M.rollerSupport(19.0, 14.9, 2.3, f.lite)
    ].join('');
  });

  /* ── 3 Settings ──────────────────────────────────────────────── */

  /* A cross-section with hatching: the constants on this component (E, nu, A, I0, k) are
     properties of the material the section is cut from, and hatching is how a section says
     "this is stuff", not "this is a shape". */
  def(2, 'Material', function (f) {
    var x = 5.4, y = 4.6, w = 13.2, h = 14.8, out = [M.rect(x, y, w, h, 1, { fill: W })], i, t;
    for (i = 1; i < 7; i++) {
      t = (w + h) * i / 7;
      out.push(M.line(x + Math.max(0, t - h), y + Math.min(h, t),
        x + Math.min(w, t), y + Math.max(0, t - w), { stroke: f.base, sw: V.HAIR }));
    }
    out.push(M.rect(x, y, w, h, 1, { fill: 'none' }));
    return out.join('');
  });

  /* The loss curve the optimizer descends, with the set's settings badge. */
  def(2, 'Optimizer_Settings', function (f) {
    return [M.rect(1.8, 2.6, 13.8, 12.8, 1.2, { fill: W }), M.plot(3.8, 4.4, 9.8, 9.2, f.base), B.gear(f)].join('');
  });

  /* The engine is the CONTAINER the solver runs in, not a structural idea at all — hence the
     chip, and hence Tooling grey. */
  def(2, 'Engine', function (f) {
    return [M.chip(2.6, 2.6, 12.4, f.base), B.gear(f)].join('');
  }, 'tool');

  /* ── 4 Run ───────────────────────────────────────────────────── */

  /* Three members of growing depth on one axis: the optimizer moves I where the forces are, and
     depth IS I here. The play mark says it runs. */
  def(3, 'Optimize', function (f) {
    return [
      M.member(2.2, 8.6, 7.0, 8.6, 1.8, W),
      M.member(7.0, 8.6, 11.4, 8.6, 3.4, f.lite),
      M.member(11.4, 8.6, 15.4, 8.6, 5.2, f.base),
      M.circle(16.8, 16.8, 5.4, { fill: W }),
      M.play(16.8, 16.8, 3.4, f.base)
    ].join('');
  });

  /* A trained network answering instead of the solver: the net, with the play mark. */
  def(3, 'Predict', function (f) {
    return [
      M.nodes(2.6, 3.4, 11.4, 10.6, f.base),
      M.circle(16.8, 16.8, 5.4, { fill: W }),
      M.play(16.8, 16.8, 3.4, f.base)
    ].join('');
  }, 'ml');

  /* Stacked samples — many solved beams — with the play mark: this component MAKES the dataset
     the other two consume. */
  def(3, 'Generate_Data', function (f) {
    return [
      M.dataset(2.6, 3.4, 11.8, 3, 1.5, f.lite),
      M.circle(16.8, 16.8, 5.4, { fill: W }),
      M.play(16.8, 16.8, 3.4, f.base)
    ].join('');
  }, 'ml');

  /* The same net as Predict, with the sparkle that marks learning in this set — the difference
     between a model being USED and a model being MADE. */
  def(3, 'Train_Surrogate', function (f) {
    return [M.nodes(4.6, 5.4, 12.4, 11.4, f.base), M.spark(4.2, 4.2, 3.0, f.dark)].join('');
  }, 'ml');

  /* ── 5 Results ───────────────────────────────────────────────── */

  /* The run taken apart: the member above, its per-element numbers pulled out as rows below. */
  def(4, 'Deconstruct_Result', function (f) {
    return [
      M.member(2.2, 4.4, 15.4, 4.4, 2.6, W),
      M.bars(2.6, 15.8, 12.2, [3.4, 6.6, 4.8, 7.8], f.base, f.lite),
      B.out(f)
    ].join('');
  });

  /* What the plugin draws back into Rhino: the optimized member over its bending diagram. The
     parabola is filled because the diagram is a field, not a line chart. */
  def(4, 'Visualize_Result', function (f) {
    var x = 2.4, y = 9.8, w = 12.8, h = 2.8,
      out = [
        M.member(2.2, 5.2, 15.4, 5.2, 3.0, f.lite),
        M.line(2.2, y, 15.4, y, { sw: V.HAIR }),
        M.bending(x, y, w, h, f.lite)
      ], i, t;
    for (i = 1; i <= 3; i++) {
      t = i / 4;
      out.push(M.line(x + w * t, y, x + w * t, y + 4 * h * t * (1 - t), { stroke: f.base, sw: V.HAIR }));
    }
    out.push(B.eye(f));
    return out.join('');
  });

  /* The product mark: the ribbon tab's icon and the assembly's, with no component behind it.
     Three members of growing depth and nothing else — the one idea the plugin is about, which is
     also why Optimize builds on the same shape. */
  def(5, 'OpenPyStruct', function (f) {
    return [
      M.member(3.0, 12.0, 9.0, 12.0, 2.6, W),
      M.member(9.0, 12.0, 15.0, 12.0, 5.0, f.lite),
      M.member(15.0, 12.0, 21.0, 12.0, 7.6, f.base)
    ].join('');
  });

  root.OpsVecSet = { TABS: TABS, ICONS: S, ORDER: ORDER };
})(typeof window !== 'undefined' ? window : globalThis);
