import type { NextRequest } from "next/server";
export const runtime = "nodejs";
const permitted = new Set(["GET auth/csrf", "POST auth/register", "POST auth/login", "POST auth/logout", "POST auth/verify-email", "POST auth/resend-verification", "GET users/me", "GET categories"]);
const uuid = "[0-9a-f-]{36}";
const profile = new RegExp("^organizations/" + uuid + "/provider-profile$", "i");
const organization = new RegExp("^organizations/" + uuid + "(/members)?$", "i");
const invitations = new RegExp("^organizations/" + uuid + "/invitations$", "i");
const member = new RegExp("^organizations/" + uuid + "/members/" + uuid + "$", "i");
const verificationList = new RegExp("^organizations/" + uuid + "/verifications$", "i");
const verificationDetail = new RegExp("^organizations/" + uuid + "/verifications/" + uuid + "$", "i");
const verificationMutation = new RegExp("^organizations/" + uuid + "/verifications/" + uuid + "/(documents|submit|revisions)$", "i");
const documentRemoval = new RegExp("^organizations/" + uuid + "/verifications/" + uuid + "/documents/" + uuid + "$", "i");
const documentAccess = new RegExp("^verification-documents/" + uuid + "/access$", "i");
const documentContent = new RegExp("^verification-documents/" + uuid + "/content$", "i");
const adminQueue = /^admin\/verifications$/;
const adminDetail = new RegExp("^admin/verifications/" + uuid + "$", "i");
const adminAction = new RegExp("^admin/verifications/" + uuid + "/(start-review|decisions)$", "i");
const notification = new RegExp("^notifications/" + uuid + "$", "i");
const publicProvider = new RegExp("^providers/" + uuid + "$", "i");
const publication = new RegExp("^organizations/" + uuid + "/provider-profile/publication$", "i");
const publicService = new RegExp("^services/" + uuid + "$", "i");
const catalogList = new RegExp("^organizations/" + uuid + "/services$", "i");
const catalogDetail = new RegExp("^organizations/" + uuid + "/services/" + uuid + "$", "i");
const catalogAction = new RegExp("^organizations/" + uuid + "/services/" + uuid + "/(publication|archive)$", "i");
class PayloadTooLarge extends Error {}
async function readBody(request: NextRequest): Promise<ArrayBuffer | undefined> {
    if (request.method === "GET" || !request.body) return undefined;
    const limit = 11 * 1024 * 1024;
    if (Number(request.headers.get("content-length") ?? 0) > limit) throw new PayloadTooLarge();
    const reader = request.body.getReader(); const chunks: Uint8Array[] = []; let total = 0;
    try {
        for (;;) {
            const next = await reader.read(); if (next.done) break;
            total += next.value.byteLength;
            if (total > limit) { await reader.cancel(); throw new PayloadTooLarge(); }
            chunks.push(next.value);
        }
    } finally { reader.releaseLock(); }
    const bytes = new Uint8Array(total); let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    return bytes.buffer;
}
async function forward(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
    const { path } = await context.params; const route = path.join("/"); const method = request.method;
    const allowed = permitted.has(method + " " + route)
        || (method === "POST" && (route === "organizations" || route === "invitations/accept" || invitations.test(route) || verificationList.test(route) || verificationMutation.test(route) || documentAccess.test(route) || adminAction.test(route) || publication.test(route) || catalogList.test(route) || catalogAction.test(route)))
        || (method === "GET" && (organization.test(route) || verificationList.test(route) || verificationDetail.test(route) || documentContent.test(route) || adminQueue.test(route) || adminDetail.test(route) || route === "notifications" || route === "providers" || publicProvider.test(route) || route === "services" || publicService.test(route) || catalogList.test(route) || catalogDetail.test(route)))
        || (["GET", "PUT"].includes(method) && profile.test(route))
        || (method === "PUT" && catalogDetail.test(route))
        || (method === "PATCH" && notification.test(route))
        || (method === "DELETE" && (member.test(route) || documentRemoval.test(route)));
    if (!allowed) return Response.json({ title: "Resource unavailable" }, { status: 404 });
    if (method !== "GET" && request.headers.get("origin") !== (process.env.WEB_ORIGIN ?? "http://localhost:3000")) return Response.json({ title: "Invalid request origin" }, { status: 403 });
    const headers = new Headers();
    for (const name of ["cookie", "content-type", "x-csrf-token", "if-match", "x-evidence-access"]) {
        const value = request.headers.get(name); if (value) headers.set(name, value);
    }
    try {
        const body = await readBody(request);
        const upstream = await fetch(new URL("/api/v1/" + route + request.nextUrl.search, process.env.API_BASE_URL ?? "http://localhost:5080"), {
            method, headers, body, cache: "no-store", redirect: "manual", signal: AbortSignal.timeout(120000)
        });
        const responseHeaders = new Headers({ "Cache-Control": "no-store", "Referrer-Policy": "no-referrer" });
        for (const name of ["content-type", "retry-after", "etag", "content-disposition", "x-content-type-options", "content-security-policy"]) {
            const value = upstream.headers.get(name); if (value) responseHeaders.set(name, value);
        }
        for (const cookie of upstream.headers.getSetCookie()) responseHeaders.append("Set-Cookie", cookie);
        return new Response(upstream.status === 204 ? null : await upstream.arrayBuffer(), { status: upstream.status, headers: responseHeaders });
    } catch (error) {
        return Response.json({ title: error instanceof PayloadTooLarge ? "Upload request exceeds 11 MiB." : "The service is temporarily unavailable." }, { status: error instanceof PayloadTooLarge ? 413 : 503 });
    }
}
export const GET = forward; export const POST = forward; export const DELETE = forward; export const PUT = forward;


export const PATCH = forward;
