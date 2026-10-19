import { mount } from "svelte";
import "./app.css";
import App from "./App.svelte";

// El bundle es un script clásico (no un módulo): si por lo que sea se cargara
// antes de tiempo, esperamos a que el contenedor exista. Nada se inventa aquí:
// sin #app simplemente no se monta nada y se avisa en el registro.
function boot() {
  const target = document.getElementById("app");
  if (!target) {
    console.error("TempPanel: no se encontró el contenedor #app");
    return;
  }
  mount(App, { target });
}

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", boot, { once: true });
} else {
  boot();
}
