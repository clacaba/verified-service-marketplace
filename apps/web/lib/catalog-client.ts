export type ServiceImage = { id: string; altText: string; width: number; height: number; size: number; createdAt: string; contentType: "image/png"; url: string };
export type ListingInput = { title: string; description: string; categoryId: string; priceMinor: number; currency: "PHP"; durationMinutes: number; serviceArea: string };
export type Listing = ListingInput & { id: string; organizationId: string; status: "Draft" | "Published" | "Archived"; version: string; updatedAt: string; images: ServiceImage[] };
export type PublicListing = {
    id: string; title: string; description: string; priceMinor: number; currency: string; durationMinutes: number; serviceArea: string;
    images: ServiceImage[]; category: {id: string; name: string}; provider: {id: string; displayName: string}; disclosure: string;
};
export class CatalogError extends Error { constructor(message: string, public status: number) { super(message); } }
export async function catalogRequest<T>(path: string, method = "GET", input?: unknown, etag?: string): Promise<{ data: T; etag: string }> {
    const headers: Record<string, string> = {};
    if (method !== "GET") {
        const response = await fetch("/api/v1/auth/csrf", {cache: "no-store"});
        if (!response.ok) throw new Error("Unable to prepare a secure request.");
        headers["X-CSRF-Token"] = (await response.json()).token;
        if (!(input instanceof FormData)) headers["Content-Type"] = "application/json";
        if (etag) headers["If-Match"] = etag;
    }
    const response = await fetch("/api/v1/" + path, {method, headers, cache: "no-store", body: method === "GET" ? undefined : input instanceof FormData ? input : JSON.stringify(input ?? {})});
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new CatalogError(error.errors ? Object.values(error.errors).flat().join(" ") : error.title ?? "Unable to access the catalog.", response.status);
    }
    return {data: await response.json(), etag: response.headers.get("etag") ?? ""};
}
export function price(priceMinor: number) { return new Intl.NumberFormat("en-PH", {style: "currency", currency: "PHP"}).format(priceMinor / 100); }
