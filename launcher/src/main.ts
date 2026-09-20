import { invoke } from "@tauri-apps/api/core";
import { open } from "@tauri-apps/plugin-dialog";

type LauncherState = {
  profileId: string;
  daggerfallPath: string | null;
  clientReady: boolean;
};

const el = <T extends HTMLElement>(id: string): T => {
  const node = document.getElementById(id);
  if (!node) throw new Error(`Missing element: ${id}`);
  return node as T;
};

const statusLine = el<HTMLParagraphElement>("status");
const daggerfallPathLabel = el<HTMLElement>("daggerfall-path");
const playButton = el<HTMLButtonElement>("play");

let state: LauncherState = {
  profileId: "",
  daggerfallPath: null,
  clientReady: false
};

function setStatus(message: string, kind: "info" | "error" = "info") {
  statusLine.textContent = message;
  statusLine.dataset.kind = kind;
}

function render() {
  daggerfallPathLabel.textContent = state.daggerfallPath ?? "Not set";
  playButton.disabled = !state.clientReady;
}

async function refresh() {
  state = await invoke<LauncherState>("load_state");
  render();
}

async function browseDaggerfall() {
  const selected = await open({
    directory: true,
    title: "Select your Daggerfall folder, or the arena2 folder inside it"
  });
  if (typeof selected !== "string") return;

  try {
    await invoke("set_daggerfall_path", { path: selected });
    await refresh();
    setStatus("Daggerfall path saved.");
  } catch (error) {
    setStatus(String(error), "error");
  }
}

async function play() {
  setStatus("Launching DFMP…");
  try {
    await invoke("launch_client");
    setStatus("DFMP client launched. Choose a server from the in-game server list. Discord login happens when you connect.");
  } catch (error) {
    setStatus(String(error), "error");
  }
}

el<HTMLButtonElement>("browse-daggerfall").addEventListener("click", browseDaggerfall);
playButton.addEventListener("click", play);

refresh().then(() => {
  if (!state.clientReady) {
    setStatus(
      "DFMP client files were not found next to the launcher. Reinstall DFMP, keeping the launcher and its `client` folder together.",
      "error"
    );
  } else if (!state.daggerfallPath) {
    setStatus("Set your Daggerfall folder before playing.");
  } else {
    setStatus("Ready. Servers check Discord when you connect.");
  }
});
