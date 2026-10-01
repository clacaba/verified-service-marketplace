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
let imageUploadSeen = false; let imageReadSeen = false;
let uploadSeen = false; let contentSeen = false;
const upstream = http.createServer(async (request, response) => {
    const chunks = []; for await (const chunk of request) chunks.push(chunk);
    const body = Buffer.concat(chunks);
    if (request.url.endsWith("/images") && request.method === "POST") {
        imageUploadSeen = body.includes(evidenceBytes) && request.headers["if-match"] === '"version-one"' && request.headers["x-csrf-token"] === "test-csrf" && request.headers["content-type"].startsWith("multipart/form-data;");
        response.setHeader("ETag", '"image-version"'); response.setHeader("Content-Type", "application/json"); response.end("{}"); return;
    }
    if (request.url.includes("/images/") && request.url.endsWith("/content")) {
        imageReadSeen = true; response.setHeader("Content-Type", "image/png"); response.setHeader("X-Content-Type-Options", "nosniff");
        response.setHeader("Content-Security-Policy", "default-src 'none'; sandbox"); response.setHeader("Content-Disposition", 'inline; filename="service-image.png"'); response.end(evidenceBytes); return;
    }
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
    if (request.url.startsWith("/api/v1/categories")) {
        response.setHeader("Content-Type", "application/json");
        response.end(JSON.stringify({items: [{id: organization, name: "Cleaning"}], page: 1, pageSize: 100, totalCount: 1})); return;
    }
    if (request.url.startsWith("/api/v1/services")) {
        response.setHeader("Content-Type", "application/json");
        const offer = {id: application, title: "Demo cleaning", description: "Fictional offer.", priceMinor: 25000, images: [{id: document, altText: "Fictional equipment", width: 80, height: 40, size: 200, createdAt: "2026-10-01T00:00:00Z", contentType: "image/png", url: "/api/v1/services/" + application + "/images/" + document + "/content"}], currency: "PHP", durationMinutes: 60, serviceArea: "Demo area", category: {id: organization, name: "Cleaning"}, provider: {id: organization, displayName: "Demo Provider"}, disclosure: "Fictional offer with simulated checks."};
        response.end(JSON.stringify(request.url.includes("/services/") ? offer : {items: [offer], totalCount: 1})); return;
    }
    if (request.url.startsWith("/api/v1/providers")) {
        response.setHeader("Content-Type", "application/json");
        const provider = { id: organization, displayName: "Demo Provider", providerType: "Individual", biography: "Fictional services.", serviceArea: "Demo area", categories: [{id: organization, name: "Cleaning"}], badges: [{type: "Identity", label: "Identity evidence reviewed (simulated)", expiresAt: "2099-01-01T00:00:00Z", simulated: true}], disclosure: "Fictional simulated checks only." };
        response.end(JSON.stringify(request.url.includes("/providers/") ? provider : {items: [provider], totalCount: 1})); return;
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
    assert.equal((await fetch(origin + "/admin/verifications")).status, 200);
    assert.equal((await fetch(origin + "/notifications")).status, 200);
    assert.equal((await fetch(origin + "/api/v1/admin/verifications?status=Submitted")).status, 200);
    for (const action of ["start-review", "decisions"]) {
        assert.equal((await fetch(origin + "/api/v1/admin/verifications/" + application + "/" + action, { method: "POST", headers: { Origin: origin, "Content-Type": "application/json", "X-CSRF-Token": "test-csrf", "If-Match": '"version-one"' }, body: "{}" })).status, 200);
    }
    assert.equal((await fetch(origin + "/api/v1/organizations/" + organization + "/verifications/" + application + "/revisions", { method: "POST", headers: { Origin: origin, "Content-Type": "application/json", "X-CSRF-Token": "test-csrf" }, body: "{}" })).status, 200);
    assert.equal((await fetch(origin + "/api/v1/notifications/" + document, { method: "PATCH", headers: { Origin: origin, "Content-Type": "application/json", "X-CSRF-Token": "test-csrf" }, body: '{"read":true}' })).status, 200);
    assert.equal((await fetch(origin + "/api/v1/admin/unknown")).status, 404);
    const directory = await fetch(origin + "/providers"); assert.equal(directory.status, 200);
    assert.match(await directory.text(), /Identity evidence reviewed \(simulated\)/);
    const publicProfile = await fetch(origin + "/providers/" + organization); assert.equal(publicProfile.status, 200);
    assert.match(await publicProfile.text(), /not a background check/);
    assert.equal((await fetch(origin + "/api/v1/providers?pageSize=1")).status, 200);
    assert.equal((await fetch(origin + "/api/v1/providers/" + organization)).status, 200);
    assert.equal((await fetch(origin + "/api/v1/providers/" + organization, {method: "POST", headers: {Origin: origin}})).status, 404);
    assert.equal((await fetch(origin + "/api/v1/organizations/" + organization + "/provider-profile/publication", { method: "POST", headers: {Origin: origin, "Content-Type": "application/json", "X-CSRF-Token": "test-csrf", "If-Match": '"version-one"'}, body: '{"published":true}' })).status, 200);
    assert.equal((await fetch(origin + "/catalog?organizationId=" + organization)).status, 200);
    const services = await fetch(origin + "/services?q=cleaning&categoryId=&minPriceMinor="); assert.equal(services.status, 200);
    assert.ok((await services.text()).includes("Demo cleaning"));
    const servicePage = await fetch(origin + "/services/" + application); assert.equal(servicePage.status, 200);
    assert.ok((await servicePage.text()).includes("Fictional offer"));
    assert.equal((await fetch(origin + "/services/not-a-uuid")).status, 404);
    assert.equal((await fetch(origin + "/api/v1/services?sort=priceAsc")).status, 200);
    assert.equal((await fetch(origin + "/api/v1/services/" + application)).status, 200);
    const catalogRoot = origin + "/api/v1/organizations/" + organization + "/services";
    assert.equal((await fetch(catalogRoot)).status, 200);
    assert.equal((await fetch(catalogRoot + "/" + application)).status, 200);
    const secure = {Origin: origin, "Content-Type": "application/json", "X-CSRF-Token": "test-csrf", "If-Match": '"version-one"'};
    assert.equal((await fetch(catalogRoot, {method: "POST", headers: secure, body: "{}"})).status, 200);
    assert.equal((await fetch(catalogRoot + "/" + application, {method: "PUT", headers: secure, body: "{}"})).status, 200);
    for (const action of ["publication", "archive"]) assert.equal((await fetch(catalogRoot + "/" + application + "/" + action, {method: "POST", headers: secure, body: "{}"})).status, 200);
    assert.equal((await fetch(origin + "/api/v1/services/" + application, {method: "PUT", headers: secure, body: "{}"})).status, 404);
    assert.equal((await fetch(catalogRoot, {method: "POST", headers: {...secure, Origin: "https://untrusted.example"}, body: "{}"})).status, 403);
    const imageForm = new FormData(); imageForm.append("file", new Blob([evidenceBytes], {type: "image/png"}), "demo.png"); imageForm.append("altText", "Fictional equipment"); imageForm.append("demoAcknowledged", "true");
    const imageUpload = await fetch(catalogRoot + "/" + application + "/images", {method: "POST", headers: {Origin: origin, "X-CSRF-Token": "test-csrf", "If-Match": '"version-one"'}, body: imageForm});
    assert.equal(imageUpload.status, 200); assert.ok(imageUploadSeen); assert.equal(imageUpload.headers.get("etag"), '"image-version"');
    for(const url of [origin + "/api/v1/services/" + application + "/images/" + document + "/content", catalogRoot + "/" + application + "/images/" + document + "/content"]) {
        const imageRead = await fetch(url); assert.equal(imageRead.status, 200);
        assert.deepEqual(Buffer.from(await imageRead.arrayBuffer()), evidenceBytes); assert.equal(imageRead.headers.get("content-type"), "image/png"); assert.equal(imageRead.headers.get("cache-control"), "no-store"); assert.equal(imageRead.headers.get("x-content-type-options"), "nosniff");
    }
    assert.ok(imageReadSeen);
    assert.equal((await fetch(catalogRoot + "/" + application + "/images/" + document, {method: "DELETE", headers: secure, body: "{}"})).status, 200);
    assert.equal((await fetch(origin + "/api/v1/services/" + application + "/images", {method: "POST", headers: secure, body: "{}"})).status, 404);
    assert.equal((await fetch(catalogRoot + "/" + application + "/images", {method: "POST", headers: {...secure, Origin: "https://untrusted.example"}, body: "{}"})).status, 403);
    assert.equal((await fetch(catalogRoot + "/" + application + "/images", {method: "POST", headers: {...secure, "Content-Length": String(6 * 1024 * 1024 + 1)}, body: "x".repeat(6 * 1024 * 1024 + 1)})).status, 413);
    console.log("PASS: image upload/removal, binary public/private delivery, image limit/origin/allowlist protections; catalog editor, public service pages and authorized proxy methods; public directory/detail content, publication/read-only proxy allowlist; reviewer/notification pages and allowed review/revision/PATCH proxy routes; verification page HTTP 200, multipart byte/header forwarding, binary private download, ETag/no-store forwarding, origin protection, and route allowlist.");
} finally {
    const stopped = once(web, "exit"); web.kill(); await stopped;
    await new Promise(done => upstream.close(done));
}
