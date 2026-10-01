export type Profile = { id: string; organizationId: string; displayName: string; providerType: "Individual" | "Business"; biography: string; categoryIds: string[]; serviceArea: string; imageId: null; version: string; status: "Draft"; publishedAt: string | null; readyForVerification: boolean };
export class ProfileError extends Error { constructor(message: string, public status: number) { super(message); } }
export async function profileRequest(organizationId: string, input?: Profile, etag?: string): Promise<{ profile: Profile; etag: string }> {
    const headers: Record<string, string> = {};
    if (input) {
        const csrf = await fetch("/api/v1/auth/csrf", { cache: "no-store" });
        if (!csrf.ok) throw new Error("Unable to prepare a secure request.");
        headers["X-CSRF-Token"] = (await csrf.json()).token;
        headers["Content-Type"] = "application/json";
        if (etag) headers["If-Match"] = etag;
    }
    const response = await fetch("/api/v1/organizations/" + encodeURIComponent(organizationId) + "/provider-profile", {
        method: input ? "PUT" : "GET", headers, cache: "no-store",
        body: input ? JSON.stringify({ displayName: input.displayName, providerType: input.providerType, biography: input.biography, categoryIds: input.categoryIds, serviceArea: input.serviceArea, imageId: null }) : undefined
    });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        const fields = error.errors ? Object.values(error.errors).flat().join(" ") : "";
        throw new ProfileError(fields || error.title || "Unable to access the provider profile.", response.status);
    }
    return { profile: await response.json(), etag: response.headers.get("etag") ?? "" };
}


export async function setPublication(organizationId: string, published: boolean, etag: string) {
    const csrf = await fetch("/api/v1/auth/csrf", { cache: "no-store" });
    if (!csrf.ok) throw new Error("Unable to prepare a secure request.");
    const response = await fetch("/api/v1/organizations/" + encodeURIComponent(organizationId) + "/provider-profile/publication", {
        method: "POST", cache: "no-store",
        headers: { "Content-Type": "application/json", "X-CSRF-Token": (await csrf.json()).token, "If-Match": etag },
        body: JSON.stringify({ published })
    });
    if (!response.ok) { const error = await response.json().catch(() => ({})); throw new ProfileError(error.title ?? "Unable to change publication.", response.status); }
}
