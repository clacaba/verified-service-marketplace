import Link from "next/link";
import BookingRequest from "./booking-request";
import {notFound} from "next/navigation";
import {publicProviders} from "../../../lib/public-providers";
import {price, type PublicListing} from "../../../lib/catalog-client";
export const dynamic = "force-dynamic";
export default async function Service({params}: {params: Promise<{serviceId: string}>}) {
    const {serviceId} = await params;
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(serviceId)) notFound();
    let response: Response;
    try {response = await publicProviders("services/" + serviceId);} catch {return <main><h1>Service temporarily unavailable</h1><Link href="/services">Browse services</Link></main>;}
    if(response.status === 404) notFound();
    if(!response.ok) return <main><h1>Service temporarily unavailable</h1><Link href="/services">Browse services</Link></main>;
    const s: PublicListing = await response.json();
    return <main className="auth-page"><Link href="/services">Browse services</Link><h1>{s.title}</h1>
        <p><Link href={"/providers/" + s.provider.id} prefetch={false}>{s.provider.displayName}</Link> · {s.category.name}</p>
        <p>{price(s.priceMinor)} fixed price · {s.durationMinutes} minutes · {s.serviceArea}</p>
        <section aria-label="Service photos">{s.images.map(image => <figure key={image.id}><img src={image.url} alt={image.altText} width={image.width} height={image.height} style={{maxWidth: "100%", height: "auto"}}/><figcaption>{image.altText}</figcaption></figure>)}</section>
        <p style={{whiteSpace: "pre-wrap"}}>{s.description}</p><p>{s.disclosure}</p>
        <BookingRequest serviceId={serviceId}/><p>Payments and reviews are not implemented yet.</p>
    </main>;
}
