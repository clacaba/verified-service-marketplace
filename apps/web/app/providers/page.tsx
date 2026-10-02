import Link from "next/link";
import { publicProviders, type PublicProvider } from "../../lib/public-providers";
export const dynamic = "force-dynamic";
export default async function Providers({ searchParams }: { searchParams: Promise<{ page?: string; categoryId?: string }> }) {
    const params = await searchParams;
    const page = Number(params.page ?? 1);
    if (!Number.isInteger(page) || page < 1 || page > 100000) return <main><h1>Invalid page</h1><Link href="/providers">Browse providers</Link></main>;
    const query = new URLSearchParams({ page: String(page), pageSize: "12" });
    if (params.categoryId) query.set("categoryId", params.categoryId);
    let data: { items: PublicProvider[]; totalCount: number };
    try {
        const response = await publicProviders("providers?" + query);
        if (!response.ok) throw new Error();
        data = await response.json();
    } catch { return <main><h1>Provider directory</h1><p role="alert">Unable to load providers. Please try again.</p><Link href="/">Home</Link></main>; }
    const link = (next: number) => "/providers?" + new URLSearchParams({ page: String(next), ...(params.categoryId ? { categoryId: params.categoryId } : {}) });
    return <main className="browse-page"><nav className="browse-nav" aria-label="Marketplace"><Link href="/">Marketplace</Link><Link href="/services">Find services</Link><Link href="/account">Dashboard</Link><Link href="/register">Get started ↗</Link></nav><p className="eyebrow">MEET YOUR NEXT HELPING HAND</p><h1>Provider directory</h1>
        <p className="browse-intro">Find the people behind the services. Explore profiles, service areas, and precisely described verification badges.</p><p className="demo-disclosure">Portfolio demo: fictional providers and simulated verification checks. Available services can be requested after email verification.</p>
        {data.items.length === 0 && <p>No published providers match this page. Profiles appear only after approval and explicit owner publication.</p>}
        <section className="categories" aria-label="Published providers">{data.items.map(provider => <article key={provider.id}>
            <h2><Link href={"/providers/" + provider.id} prefetch={false}>{provider.displayName}</Link></h2>
            <p>{provider.serviceArea}</p><p>{provider.categories.map(category => category.name).join(", ")}</p>
            <ul>{provider.badges.map(badge => <li key={badge.type}>{badge.label} · expires {new Date(badge.expiresAt).toISOString().slice(0, 10)} UTC</li>)}</ul>
        </article>)}</section>
        <nav aria-label="Provider pages">{page > 1 && <Link href={link(page - 1)} prefetch={false}>Previous</Link>}{" "}{page * 12 < data.totalCount && <Link href={link(page + 1)} prefetch={false}>Next</Link>}</nav>
    </main>;
}
