import Link from "next/link";
import { notFound } from "next/navigation";
import { publicProviders, type PublicProvider } from "../../../lib/public-providers";
export const dynamic = "force-dynamic";
export default async function Provider({ params }: { params: Promise<{ providerId: string }> }) {
    const { providerId } = await params;
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(providerId)) notFound();
    let response: Response;
    try { response = await publicProviders("providers/" + providerId); }
    catch { return <main><h1>Profile temporarily unavailable</h1><Link href="/providers">Browse providers</Link></main>; }
    if (response.status === 404) notFound();
    if (!response.ok) return <main><h1>Profile temporarily unavailable</h1><Link href="/providers">Browse providers</Link></main>;
    const provider: PublicProvider = await response.json();
    return <main className="auth-page"><Link href="/providers">Browse providers</Link>
        <h1>{provider.displayName}</h1><p>{provider.providerType === "Business" ? "Provider company" : "Independent provider"} · {provider.serviceArea}</p>
        <p style={{ whiteSpace: "pre-wrap" }}>{provider.biography}</p>
        <h2>Service categories</h2><ul>{provider.categories.map(category => <li key={category.id}>{category.name}</li>)}</ul>
        <h2>Current evidence checks</h2><ul>{provider.badges.map(badge => <li key={badge.type}>
            {badge.label}<br /><small>Valid until {new Date(badge.expiresAt).toISOString()} (UTC).</small>
        </li>)}</ul>
        <p>{provider.disclosure}</p><p>These checks are not a background check or a guarantee of licensing, safety, or service quality.</p>
        <p>Booking and reviews will be available in later milestones. Refresh this page to check current visibility.</p>
    </main>;
}
