// Detector de erratas en CSS: comprueba que cada bloque {...} tenga los
// parentesis balanceados (un ")" de mas o de menos invalida la declaracion
// entera y el navegador la descarta en silencio). Solo para desarrollo.
//
// Revisa el CSS COMPILADO (Frontend/assets/style.css) y, si existe, tambien la
// hoja fuente (Web/src/app.css): asi se pilla la errata antes de compilar.
const fs = require("fs");
const files = process.argv.slice(2).filter((a) => !a.startsWith("--"));
const list = files.length
  ? files
  : ["C:/tempanel/TempPanel/Frontend/assets/style.css", "C:/tempanel/TempPanel/Web/src/app.css"];
let totalBad = 0;
for (const file of list) {
  if (!fs.existsSync(file)) { console.log("(no existe: " + file + ")"); continue; }
  const css = fs.readFileSync(file, "utf8");
  let bad = 0, rules = 0, line = 1, depth = 0, start = -1;
  for (let i = 0; i < css.length; i++) {
    const ch = css[i];
    if (ch === "\n") line++;
    if (ch === "{") { rules++; depth = 0; start = line; }
    else if (ch === "}") {
      if (depth !== 0) { bad++; console.log("DESBALANCE en la regla que empieza en la linea " + start + ": parens sin cerrar = " + depth); }
      depth = 0;
    }
    else if (ch === "(") depth++;
    else if (ch === ")") depth--;
    if (depth < 0) { bad++; console.log("')' de mas en la linea " + line); depth = 0; }
  }
  console.log(file.split("/").pop() + ": " + rules + " bloques revisados, " + bad + " problemas");
  totalBad += bad;
}
process.exitCode = totalBad ? 1 : 0;
