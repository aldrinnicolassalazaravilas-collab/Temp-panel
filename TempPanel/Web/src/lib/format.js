// Utilidades de formato. Los datos internos siempre están en °C y en unidades
// del sistema; aquí solo se convierten para mostrarlos. Nada se redondea de más:
// null/undefined = "No disponible" (nunca 0 ni un valor inventado).

export const isNil = (v) => v === null || v === undefined;

/** Texto seguro para valores que pueden faltar. */
export const txt = (v) => (isNil(v) || v === "" ? "No disponible" : String(v));

/** Número con decimales o null si no hay lectura. */
export const num = (v, d = 1) => (isNil(v) ? null : Number(v).toFixed(d));

export const unitSym = (unit) => (unit === "F" ? "°F" : "°C");
export const toUnit = (c, unit) => (unit === "F" ? (c * 9) / 5 + 32 : c);

/** Temperatura con unidad o "No disponible". */
export const deg = (c, unit, d = 1) =>
  isNil(c) ? "No disponible" : `${toUnit(c, unit).toFixed(d)} ${unitSym(unit)}`;

/** Temperatura con 1 decimal y su unidad (la que se muestra en toda la app). */
export const degT = (c, unit) => deg(c, unit, 1);

/** Promedio de una lista con huecos (null si no hay ningún valor). */
export function avg(arr) {
  if (!arr || !arr.length) return null;
  const v = arr.filter((x) => !isNil(x));
  return v.length ? v.reduce((a, b) => a + b, 0) / v.length : null;
}

/** Clase CSS según umbrales (vacío si no hay lectura). */
export function tempClass(v, warn, crit) {
  if (isNil(v)) return "";
  if (v >= crit) return "crit-t";
  if (v >= warn) return "warn-t";
  return "ok-t";
}

/** Color de la serie/barras según umbrales. */
export function tempColor(v, warn, crit, base = "#22d3ee") {
  if (isNil(v)) return base;
  if (v >= crit) return "#f87171";
  if (v >= warn) return "#fbbf24";
  return base;
}

/** Nombre corto de núcleo: "CPU Core #3" -> "N3", "P-Core #2 Thread 1" -> "P2·H1". */
export function shortName(n) {
  return String(n)
    .replace("CPU Core ", "N")
    .replace("P-Core #", "P")
    .replace("E-Core #", "E")
    .replace("Core #", "N")
    .replace("Core ", "N")
    .replace(" Thread ", "·H")
    .replace(/#/g, "");
}

/** Números grandes con separador español: 1234 -> "1.234". */
export const nf = (v, d = 0) =>
  isNil(v) ? "No disponible" : Number(v).toLocaleString("es-ES", { minimumFractionDigits: d, maximumFractionDigits: d });

/** Estado corto y honesto de la batería, con la tendencia medida (no predicha). */
export function batTrendLabel(b) {
  if (!b) return "";
  if (b.held && b.heldMinutes != null) return `${b.status} · sin cambios ${Math.round(b.heldMinutes)} min`;
  if (b.trendPctPerHour != null && Math.abs(b.trendPctPerHour) >= 0.05)
    return `${b.status} · ${b.trendPctPerHour > 0 ? "▲ +" : "▼ "}${b.trendPctPerHour.toFixed(2)} %/h (observado)`;
  return b.status;
}

/** Hexadecimal + alfa -> rgba(). */
export function hexA(hex, a) {
  const n = parseInt(hex.slice(1), 16);
  return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
}

/** Mezcla dos colores hex (0 = a, 1 = b). Solo para el fondo decorativo. */
export function mixHex(a, b, t) {
  const na = parseInt(a.slice(1), 16), nb = parseInt(b.slice(1), 16);
  const r = Math.round(((na >> 16) & 255) * (1 - t) + ((nb >> 16) & 255) * t);
  const g = Math.round(((na >> 8) & 255) * (1 - t) + ((nb >> 8) & 255) * t);
  const bl = Math.round((na & 255) * (1 - t) + (nb & 255) * t);
  return `rgb(${r} ${g} ${bl})`;
}

export const clamp01 = (v) => Math.max(0, Math.min(1, v));
export const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
