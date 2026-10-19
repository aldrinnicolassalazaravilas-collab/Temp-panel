// Motor de dibujo de las gráficas (canvas). Funciones puras: sin estado global.

/** Curva suave (Catmull-Rom -> Bézier) sobre una lista de puntos [x,y]. */
export function traceSmooth(ctx, pts) {
  if (!pts.length) return;
  ctx.moveTo(pts[0][0], pts[0][1]);
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[Math.max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(pts.length - 1, i + 2)];
    ctx.bezierCurveTo(
      p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6,
      p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6,
      p2[0], p2[1]);
  }
}

/** Trocea la serie en tramos contiguos: los huecos (null) no se unen con líneas. */
export function segments(series, X, Y) {
  const segs = [];
  let cur = [];
  series.forEach((v, i) => {
    if (v === null || v === undefined) { if (cur.length) segs.push(cur); cur = []; return; }
    cur.push([X(i), Y(v)]);
  });
  if (cur.length) segs.push(cur);
  return segs;
}

/** Mínimo, máximo y promedio reales de una serie con huecos (null si no hay datos). */
export function statsOf(values) {
  const pts = (values || []).filter((v) => v !== null && v !== undefined);
  if (!pts.length) return null;
  return {
    min: Math.min(...pts),
    max: Math.max(...pts),
    avg: pts.reduce((a, b) => a + b, 0) / pts.length,
    n: pts.length
  };
}

/** Hora legible de un instante (Date o cadena ISO). */
export function timeLabel(t) {
  if (t === null || t === undefined) return "";
  const d = t instanceof Date ? t : new Date(t);
  return isNaN(d.getTime()) ? String(t) : d.toLocaleTimeString("es", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
}
