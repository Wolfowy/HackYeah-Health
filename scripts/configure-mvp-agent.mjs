import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
process.loadEnvFile(path.join(root, "Backend/.env"));
const apiKey = process.env.ELEVENLABS_API_KEY;
const agentId = process.env.ELEVENLABS_AGENT_ID;
if (!apiKey || !agentId)
  throw new Error("Brak konfiguracji ElevenLabs w Backend/.env");
const description = fs
  .readFileSync(path.join(root, "docs/elevenlabs-interview-json.md"), "utf8")
  .match(/```text\n([\s\S]*?)\n```/)[1];
async function request(route, method = "GET", body) {
  const response = await fetch(`https://api.elevenlabs.io${route}`, {
    method,
    headers: { "xi-api-key": apiKey, "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(20000),
  });
  if (!response.ok)
    throw new Error(
      `ElevenLabs HTTP ${response.status} (${method} ${route.split("?")[0]})`,
    );
  return response.json();
}
const route = `/v1/convai/agents/${encodeURIComponent(agentId)}`;
const before = await request(route);
const existing = before.platform_settings.data_collection ?? {};
if (!process.argv.includes("--apply")) {
  console.log(
    JSON.stringify({
      configured: !!existing.interview_json,
      matches: existing.interview_json?.description === description,
    }),
  );
} else {
  // Save a private recovery copy without including the API key.
  const backupDir = fs.mkdtempSync("/private/tmp/docprep-mvp-agent-");
  fs.chmodSync(backupDir, 0o700);
  fs.writeFileSync(
    path.join(backupDir, "before.json"),
    JSON.stringify(before, null, 2),
    { mode: 0o600 },
  );
  const updated = await request(route, "PATCH", {
    platform_settings: {
      data_collection: {
        ...existing,
        interview_json: { type: "string", description },
      },
    },
    version_description:
      "DocPrep MVP: typowany interview_json dla importu do draftu",
  });
  const after = await request(route);
  if (
    after.platform_settings.data_collection?.interview_json?.description !==
    description
  )
    throw new Error("Nie potwierdzono zapisu reguły interview_json");
  for (const key of Object.keys(existing)) {
    if (!after.platform_settings.data_collection[key])
      throw new Error(`Utracono wcześniejsze pole ${key}`);
  }
  console.log(
    JSON.stringify({
      configured: true,
      preservedFields: Object.keys(existing).length,
      backupDir,
      versionId:
        updated.version_id ?? updated.version_metadata?.version_id ?? null,
    }),
  );
}
