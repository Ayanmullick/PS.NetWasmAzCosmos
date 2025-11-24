import { dotnet } from "./_framework/dotnet.js";

const setMessage = (message) => {
  const element = document.getElementById("message");
  if (element) {
    element.textContent = message;
  }
};

try {
  const { getAssemblyExports, runMain } = await dotnet
    .withApplicationArgumentsFromQuery()
    .create();

  await runMain();

  const exports = await getAssemblyExports("HelloWasm");

  const response = await fetch("./Hello.ps1");
  if (!response.ok) {
      throw new Error(`Failed to fetch Hello.ps1: ${response.statusText}`);
  }
  const script = await response.text();

  setMessage("Executing PowerShell...");
  await new Promise(r => setTimeout(r, 50));

  const output = exports.HelloWasmApp.Interop.ExecutePowerShell(script);
  setMessage(output);
} catch (error) {
  console.error("Failed to start .NET runtime", error);
  setMessage("Runtime error. Check console.");
}
