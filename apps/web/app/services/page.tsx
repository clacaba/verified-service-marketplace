import Link from "next/link";
import { publicProviders } from "../../lib/public-providers";
import { price, type PublicListing } from "../../lib/catalog-client";
export const dynamic = "force-dynamic";
export default async function Services({searchParams}: {searchParams: Promise<Record<string, string | string[] | undefined>>}) {
    const params = await searchParams;
    const allowed = ["q", "categoryId", "providerId", "area", "minPriceMinor", "maxPriceMinor", "sort", "page"];
    const query = new URLSearchParams({pageSize: "12"});
    for (const key of allowed) if (typeof params[key] === "string" && (params[key] as string).trim()) query.set(key, params[key] as string);
    const page = Number(query.get("page") ?? 1);
    let data: {items: PublicListing[]; totalCount: number} | null = null; let error = "";
    let categories: {id: string; name: string}[] = [];
    try {
        const response = await publicProviders("services?" + query);
        if (!response.ok) error = response.status === 400 ? "Invalid filters. Check your prices, page, or category." : "Catalog temporarily unavailable.";
        else data = await response.json();
        const categoryResponse = await publicProviders("categories?pageSize=100"); if(categoryResponse.ok) categories = (await categoryResponse.json()).items;
    } catch {error = "Catalog temporarily unavailable. Try again.";}
    const value = (key: string) => typeof params[key] === "string" ? params[key] as string : "";
    const pageLink = (next: number) => {const result = new URLSearchParams(query); result.set("page", String(next)); return "/services?" + result;};
    return <main className="browse-page"><nav className="browse-nav" aria-label="Marketplace"><Link href="/">Marketplace</Link><Link href="/providers">Meet providers</Link><Link href="/account">Dashboard</Link><Link href="/register">Get started ↗</Link></nav><p className="eyebrow">MAKE SPACE FOR THE GOOD STUFF</p><h1>Find a service</h1><p className="browse-intro">A little help for your home. Explore services, compare prices, and choose your next fresh start.</p><p className="demo-disclosure">Fictional demo offers from providers with current simulated checks. Verify your email and add a fictional property to request an available time.</p>
        <form action="/services" className="profile-fields">
            <label htmlFor="q">Search title or description</label><input id="q" name="q" maxLength={150} defaultValue={value("q")}/>
            <label htmlFor="categoryId">Category</label><select id="categoryId" name="categoryId" defaultValue={value("categoryId")}><option value="">All categories</option>{categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select>
            <label htmlFor="area">Area</label><input id="area" name="area" maxLength={250} defaultValue={value("area")}/>
            <label htmlFor="minPriceMinor">Minimum price (centavos)</label><input id="minPriceMinor" name="minPriceMinor" type="number" min={0} max={100000000} step={1} defaultValue={value("minPriceMinor")}/>
            <label htmlFor="maxPriceMinor">Maximum price (centavos)</label><input id="maxPriceMinor" name="maxPriceMinor" type="number" min={0} max={100000000} step={1} defaultValue={value("maxPriceMinor")}/>
            <label htmlFor="sort">Sort</label><select id="sort" name="sort" defaultValue={value("sort") || "title"}><option value="title">Title</option><option value="priceAsc">Price: low to high</option><option value="priceDesc">Price: high to low</option></select>
            {value("providerId") && <input type="hidden" name="providerId" value={value("providerId")}/>}
            <button>Search</button><Link href="/services">Clear filters</Link>
        </form>
        {error && <p role="alert">{error}</p>}
        {data && <><p>{data.totalCount} matching services</p>{data.items.length === 0 && <p>No published eligible services match these filters.</p>}
        <section className="categories" aria-label="Services">{data.items.map(s => <article key={s.id}>
            {s.images[0] && <img src={s.images[0].url} alt={s.images[0].altText} width={s.images[0].width} height={s.images[0].height} loading="lazy" style={{maxWidth: "100%", height: "auto"}}/>}
            <h2><Link href={"/services/" + s.id} prefetch={false}>{s.title}</Link></h2><p>{s.provider.displayName} · {s.category.name}</p>
            <p>{price(s.priceMinor)} · {s.durationMinutes} minutes · {s.serviceArea}</p>
        </article>)}</section><nav aria-label="Service pages">{page > 1 && <Link href={pageLink(page - 1)} prefetch={false}>Previous</Link>}{" "}{page * 12 < data.totalCount && <Link href={pageLink(page + 1)} prefetch={false}>Next</Link>}</nav></>}
    </main>;
}
