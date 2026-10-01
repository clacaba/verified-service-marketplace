import assert from "node:assert/strict";
import http from "node:http";
import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { once } from "node:events";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(resolve(root, "package.json"));
const organization = "10000000-0000-0000-0000-000000000001";
const application = "20000000-0000-0000-0000-000000000001";
const document = "30000000-0000-0000-0000-000000000001";
const evidenceBytes = Buffer.from([0, 255, 128, 13, 10, 65, 66]);
let uploadSeen = false; let contentSeen = false;
const upstream = http.createServer(async (request, response) => {
    const chunks = []; for await (const chunk of request) chunks.push(chunk);
    const body = Buffer.concat(chunks);
    if (request.url.endsWith("/documents")) {
        uploadSeen = body.includes(evidenceBytes) && request.headers["if-match"] === '"version-one"' && request.headers["x-csrf-token"] === "test-csrf" && request.headers["content-type"].startsWith("multipart/form-data;");
        response.setHeader("ETag", '"version-two"');
        response.setHeader("Content-Type", "application/json"); response.statusCode = 201;
        response.end(JSON.stringify({ id: document })); return;
    }
    if (request.url.endsWith("/content")) {
        contentSeen = request.headers["x-evidence-access"] === "test-grant";
        response.setHeader("Content-Type", "application/octet-stream");
        response.setHeader("Content-Disposition", 'attachment; filename="fictional.bin"');
        response.setHeader("X-Content-Type-Options", "nosniff");
        response.end(evidenceBytes); return;
    }
    response.setHeader("Content-Type", "application/json"); response.end("{}");
});
upstream.listen(0, "127.0.0.1"); await once(upstream, "listening");
const upstreamPort = upstream.address().port;
const portPicker = http.createServer(); portPicker.listen(0, "127.0.0.1"); await once(portPicker, "listening");
const webPort = portPicker.address().port; await new Promise(done => portPicker.close(done));
const origin = "http://localhost:" + webPort;
const web = spawn(process.execPath, [require.resolve("next/dist/bin/next"), "start", "-p", String(webPort)], {
    cwd: resolve(root, "apps/web"), windowsHide: true,
    env: { ...process.env, WEB_ORIGIN: origin, API_BASE_URL: "http://127.0.0.1:" + upstreamPort },
    stdio: ["ignore", "pipe", "pipe"]
});
let output = ""; web.stdout.on("data", chunk => { output = (output + chunk).slice(-8000); }); web.stderr.on("data", chunk => { output = (output + chunk).slice(-8000); });
try {
    let ready = false;
    for (let i = 0; i < 80; i++) {
        try { if ((await fetch(origin + "/verification?organizationId=" + organization)).status === 200) { ready = true; break; } } catch {}
        await new Promise(done => setTimeout(done, 250));
    }
    assert.ok(ready, "Next.js did not start: " + output);
    const form = new FormData(); form.append("file", new Blob([evidenceBytes], { type: "application/octet-stream" }), "fictional.bin"); form.append("evidenceType", "Identity"); form.append("demoAcknowledged", "true");
    const upload = await fetch(origin + "/api/v1/organizations/" + organization + "/verifications/" + application + "/documents", { method: "POST", headers: { Origin: origin, "If-Match": '"version-one"', "X-CSRF-Token": "test-csrf" }, body: form });
    assert.equal(upload.status, 201); assert.ok(uploadSeen, "Binary multipart bytes or security headers were changed"); assert.equal(upload.headers.get("etag"), '"version-two"');
    const content = await fetch(origin + "/api/v1/verification-documents/" + document + "/content", { headers: { "X-Evidence-Access": "test-grant" } });
    assert.deepEqual(Buffer.from(await content.arrayBuffer()), evidenceBytes); assert.ok(contentSeen); assert.equal(content.headers.get("cache-control"), "no-store"); assert.equal(content.headers.get("x-content-type-options"), "nosniff");
    const forbidden = await fetch(origin + "/api/v1/organizations/" + organization + "/verifications", { method: "POST", headers: { Origin: "https://untrusted.example", "Content-Type": "application/json" }, body: "{}" });
    assert.equal(forbidden.status, 403);
    assert.equal((await fetch(origin + "/api/v1/not-allowlisted")).status, 404);
    console.log("PASS: verification page HTTP 200, multipart byte/header forwarding, binary private download, ETag/no-store forwarding, origin protection, and route allowlist.");
} finally {
    const stopped = once(web, "exit"); web.kill(); await stopped;
    await new Promise(done => upstream.close(done));
}
