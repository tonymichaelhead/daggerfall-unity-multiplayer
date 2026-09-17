import { invoke } from "@tauri-apps/api/core";
import { open } from "@tauri-apps/plugin-dialog";

type LauncherState = {
  accountId: string;
  daggerfallPath: string | null;
  clientPath: string | null;
  hasStoredPassword: boolean;
};

const el = <T extends HTMLElement>(id: string): T => {
  const node = document.getElementById(id);
  if (!node) throw new Error(`Missing element: ${id}`);
  return node as T;
};

const loginPanel = el<HTMLElement>("login-panel");
const mainPanel = el<HTMLElement>("main-panel");
const accountInput = el<HTMLInputElement>("account");
const passwordInput = el<HTMLInputElement>("password");
const rememberInput = el<HTMLInputElement>("remember");
const statusLine = el<HTMLParagraphElement>("status");
const signedInAccount = el<HTMLElement>("signed-in-account");
const daggerfallPathLabel = el<HTMLElement>("daggerfall-path");
const clientPathLabel = el<HTMLElement>("client-path");

let state: LauncherState = {
  accountId: "",
  daggerfallPath: null,
  clientPath: null,
  hasStoredPassword: false
};

function setStatus(message: string, kind: "info" | "error" = "info") {
  statusLine.textContent = message;
  statusLine.dataset.kind = kind;
}

function render() {
  const signedIn = state.accountId.length > 0 && state.hasStoredPassword;
  loginPanel.hidden = signedIn;
  mainPanel.hidden = !signedIn;
  signedInAccount.textContent = state.accountId;
  daggerfallPathLabel.textContent = state.daggerfallPath ?? "Not set";
  clientPathLabel.textContent = state.clientPath ?? "Not set";
}

async function refresh() {
  state = await invoke<LauncherState>("load_state");
  render();
}

async function signIn() {
  const accountId = accountInput.value.trim();
  const password = passwordInput.value;

  if (!accountId || !password) {
    setStatus("Enter a username and password.", "error");
    return;
  }

  setStatus("Preparing credentials…");
  try {
    await invoke("sign_in", { accountId, password, remember: rememberInput.checked });
    passwordInput.value = "";
    await refresh();
    setStatus("Signed in. Your credentials are verified by the server when you connect.");
  } catch (error) {
    setStatus(String(error), "error");
  }
}

async function signOut() {
  await invoke("sign_out");
  await refresh();
  setStatus("Signed out.");
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

async function browseClient() {
  const selected = await open({ directory: false, title: "Select the DFMP client executable" });
  if (typeof selected !== "string") return;

  try {
    await invoke("set_client_path", { path: selected });
    await refresh();
    setStatus("Client path saved.");
  } catch (error) {
    setStatus(String(error), "error");
  }
}

async function play() {
  setStatus("Launching DFMP…");
  try {
    await invoke("launch_client");
    setStatus("DFMP client launched. Choose a server from the in-game server list.");
  } catch (error) {
    setStatus(String(error), "error");
  }
}

el<HTMLButtonElement>("sign-in").addEventListener("click", signIn);
el<HTMLButtonElement>("sign-out").addEventListener("click", signOut);
el<HTMLButtonElement>("browse-daggerfall").addEventListener("click", browseDaggerfall);
el<HTMLButtonElement>("browse-client").addEventListener("click", browseClient);
el<HTMLButtonElement>("play").addEventListener("click", play);
passwordInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter") signIn();
});

refresh().then(() => {
  if (!state.daggerfallPath) {
    setStatus("Set your Daggerfall folder before playing.");
  }
});
