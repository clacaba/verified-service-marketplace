export type EvidenceDocument = { id: string; evidenceType: string; fileName: string; contentType: string; size: number; expiresAt: string; scanStatus: string };
export type Application = { id: string; status: "Draft" | "Submitted"; version: string; submissionVersion: number; submittedAt: string | null; requiredEvidence: string[]; documents: EvidenceDocument[]; simulated: boolean };
export async function verificationRequest(path: string, method = "GET", payload?: FormData | object, etag?: string): Promise<{ data: any; etag: string }> {
    const headers: Record<string, string> = {};
    if (method !== "GET") {
        const csrf = await fetch("/api/v1/auth/csrf", { cache: "no-store" });
        if (!csrf.ok) throw new Error("Unable to prepare a secure request.");
        headers["X-CSRF-Token"] = (await csrf.json()).token;
        if (!(payload instanceof FormData)) headers["Content-Type"] = "application/json";
        if (etag) headers["If-Match"] = etag;
    }
    const response = await fetch("/api/v1/" + path, { method, headers, cache: "no-store", body: method === "GET" ? undefined : payload instanceof FormData ? payload : JSON.stringify(payload ?? {}) });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.title || "Verification request failed. Reload before retrying.");
    }
    return { data: response.status === 204 ? null : await response.json(), etag: response.headers.get("etag") ?? "" };
}
export async function downloadEvidence(document: EvidenceDocument) {
    const { data } = await verificationRequest("verification-documents/" + document.id + "/access", "POST");
    const response = await fetch(data.url, { headers: { "X-Evidence-Access": data.token }, cache: "no-store" });
    if (!response.ok) throw new Error("Document access expired or is no longer permitted.");
    const objectUrl = URL.createObjectURL(await response.blob());
    const link = window.document.createElement("a"); link.href = objectUrl; link.download = document.fileName; link.click();
    setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
}
