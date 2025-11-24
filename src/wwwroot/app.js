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

  setMessage("Executing pre-compiled PowerShell...");
  await new Promise(r => setTimeout(r, 50));

  // Execute the pre-compiled script (no fetch, no parsing)
  const output = exports.HelloWasmApp.Interop.ExecuteCompiledScript();
  setMessage(output);
} catch (error) {
  console.error("Failed to start .NET runtime", error);
  setMessage("Runtime error. Check console.");
}
