import { dotnet } from "./dotnet.js";

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
  console.log("assembly exports", exports);
  const message = exports.HelloWasmApp?.Interop?.GetMessage?.() ?? "Hello World";
  setMessage(message);
} catch (error) {
  console.error("Failed to start .NET runtime", error);
  setMessage("Runtime error. Check console.");
}
