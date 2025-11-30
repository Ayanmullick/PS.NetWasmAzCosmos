import { dotnet } from "./_framework/dotnet.js";

const setMessage = (message) => {
  const element = document.getElementById("message");
  if (!element) return;

  element.innerHTML = message;
};

try {
  const { getAssemblyExports, runMain } = await dotnet
    .withApplicationArgumentsFromQuery()
    .create();

  await runMain();

  const exports = await getAssemblyExports("PsWasmApp");

  setMessage("Executing pre-compiled PowerShell...");
  await new Promise(r => setTimeout(r, 50));

  // Execute the pre-compiled script (no fetch, no parsing)
  const output = await exports.PsWasmApp.Interop.ExecuteCompiledScriptAsync();
  const messageEl = document.getElementById("message");
  messageEl.innerHTML = "";
  const lines = output.split(/\r?\n/);
  lines.forEach((text, idx) => {
    const span = document.createElement("span");
    span.textContent = text;
    messageEl.appendChild(span);
    if (idx < lines.length - 1) {
      messageEl.appendChild(document.createElement("br"));
    }
  });
} catch (error) {
  console.error("Failed to start .NET runtime", error);
  setMessage("Runtime error. Check console.");
}
