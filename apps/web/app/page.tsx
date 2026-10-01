export const dynamic = "force-dynamic";
async function getPlatformStatus() {
  try {
    const response = await fetch(new URL("/api/v1/platform", process.env.API_BASE_URL ?? "http://localhost:5080"), { cache: "no-store", signal: AbortSignal.timeout(3000) });
    if (!response.ok) return false;
    const data = await response.json();
    return data.name === "Verified Service Marketplace";
  } catch { return false; }
}
export default async function Home() {
  const connected = await getPlatformStatus();
  return <main><header><span className="brand">Verified Service Marketplace</span><span className="tag">Development preview</span></header>
    <section className="hero"><p className="eyebrow">HOME SERVICES · BUILT ON TRUST</p><h1>Good service starts with a provider you can trust.</h1><p className="intro">A marketplace for verified cleaners, plumbers, electricians, and appliance-repair professionals.</p><a href="/properties">My properties</a> <a href="/bookings">My bookings</a> <a href="/services">Find services</a> <a href="/providers">Browse providers</a> <a href="/register">Create an account</a> <a href="/login">Sign in</a></section>
    <section className="categories" aria-label="Planned service categories">{["Cleaning","Plumbing","Electrical work","Appliance repair"].map(name => <article key={name}><h2>{name}</h2><p>Browse published services and providers. Request a fictional booking after verifying your email.</p></article>)}</section>
    <section id="roadmap"><p className="eyebrow">FIRST RELEASE</p><h2>From provider registration to verified profile.</h2><ol><li>Create an account and verify your email</li><li>Create an organization and professional profile</li><li>Submit fictional verification evidence</li><li>Receive an administrator decision</li></ol><p>Registration, organizations, private evidence, administrator review, and explicitly published provider profiles are available. All evidence checks are simulated.</p></section>
    <footer><span>API connection: {connected ? "Connected" : "Unavailable — start the backend"}</span><span>Fictional demo data only</span></footer></main>;
}
