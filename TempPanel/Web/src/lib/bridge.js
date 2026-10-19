// Puente con la app nativa (WebView2). En un navegador normal no existe:
// en ese caso la interfaz arranca igual pero sin datos (vista previa).

const wv = () => (typeof window !== "undefined" && window.chrome && window.chrome.webview) || null;

export const hasBridge = () => !!wv();

/** Envía un comando a la app WPF. Sin puente no hace nada (vista previa). */
export function post(msg) {
  const b = wv();
  if (b) b.postMessage(msg);
}

/** Registra el receptor de mensajes de la app (config, snapshot…). */
export function onMessage(fn) {
  const b = wv();
  if (b) b.addEventListener("message", (e) => fn(e.data));
  return !!b;
}
