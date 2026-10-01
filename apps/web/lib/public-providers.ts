import "server-only";
export type PublicProvider = {
    id: string; displayName: string; providerType: string; biography: string; serviceArea: string;
    categories: { id: string; name: string }[];
    badges: { type: string; label: string; expiresAt: string; simulated: boolean }[];
    disclosure: string;
};
export async function publicProviders(path: string): Promise<Response> {
    return fetch(new URL("/api/v1/" + path, process.env.API_BASE_URL ?? "http://localhost:5080"), {
        cache: "no-store", signal: AbortSignal.timeout(10000)
    });
}
