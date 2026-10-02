import Link from "next/link";

export const dynamic = "force-dynamic";
async function getPlatformStatus() {
  try {
    const response = await fetch(new URL("/api/v1/platform", process.env.API_BASE_URL ?? "http://localhost:5080"), { cache: "no-store", signal: AbortSignal.timeout(3000) });
    if (!response.ok) return false;
    return (await response.json()).name === "Verified Service Marketplace";
  } catch { return false; }
}

function HomeIllustration() {
  return <svg className="home-illustration" viewBox="0 0 560 480" fill="none" aria-hidden="true">
    <circle cx="360" cy="145" r="100" fill="#E6BA6E"/><circle cx="360" cy="145" r="77" fill="#F0CF94"/>
    <path d="M20 406C114 306 203 333 282 367C367 299 469 297 560 360V480H0V429Z" fill="#799584"/>
    <path d="M0 445C90 383 139 392 243 433C358 352 463 374 560 404V480H0Z" fill="#446C56"/>
    <path d="M138 212L284 110L427 212V403H138Z" fill="#FBF6E9"/>
    <path d="M106 219L284 94L459 219L444 240L284 128L123 240Z" fill="#234A3A"/>
    <path d="M322 153V96H357V178Z" fill="#234A3A"/>
    <path d="M157 225H410V243H157Z" fill="#E2D7C1"/>
    <rect x="170" y="267" width="65" height="71" rx="4" fill="#A7C6BA"/><path d="M202 267V338M170 300H235" stroke="#FBF6E9" strokeWidth="7"/>
    <rect x="330" y="267" width="65" height="71" rx="4" fill="#A7C6BA"/><path d="M363 267V338M330 300H395" stroke="#FBF6E9" strokeWidth="7"/>
    <path d="M257 403V298C257 261 312 261 312 298V403Z" fill="#BA7450"/><circle cx="299" cy="339" r="4" fill="#F5CF85"/>
    <path d="M254 403H316L354 480H224Z" fill="#D8C9AD"/>
    <rect x="94" y="338" width="7" height="84" rx="3" fill="#234A3A"/><ellipse cx="97" cy="310" rx="35" ry="58" fill="#325841"/>
    <rect x="465" y="300" width="8" height="126" rx="3" fill="#234A3A"/><ellipse cx="469" cy="278" rx="42" ry="73" fill="#325841"/>
    <path d="M61 428H186M390 429H519" stroke="#B6C0A6" strokeWidth="6" strokeLinecap="round"/>
    <path d="M59 136L65 122L71 136L85 142L71 148L65 162L59 148L45 142Z" fill="#F2D192"/>
  </svg>;
}

const categories = [
  {name:"Cleaning",symbol:"✧",description:"A fresh start for your space.",query:"clean"},
  {name:"Plumbing",symbol:"◉",description:"Keep the everyday flowing.",query:"plumb"},
  {name:"Electrical work",symbol:"ϟ",description:"Bright ideas. Practical help.",query:"electrical"},
  {name:"Appliance repair",symbol:"⊞",description:"Bring your essentials back to life.",query:"appliance"},
];

export default async function Home() {
  const connected = await getPlatformStatus();
  return <main className="market-home">
    <header className="market-header">
      <Link href="/" className="market-brand"><span className="brand-mark" aria-hidden="true">v.</span><span>Verified Service<span className="brand-subtitle">MARKETPLACE</span></span></Link>
      <nav aria-label="Main navigation" className="market-navigation"><Link href="/services">Find a service</Link><Link href="/providers">Meet providers</Link><Link href="#how-it-works">How it works</Link></nav>
      <div className="header-actions"><Link href="/login" className="text-link">Sign in</Link><Link href="/register" className="design-button">Get started <span aria-hidden="true">↗</span></Link></div>
    </header>
    <section className="landing-hero">
      <div className="hero-copy"><p className="hero-kicker"><span aria-hidden="true"/> A LITTLE HELP. A HAPPIER HOME.</p><h1>Less to-do.<br/>More <em>living.</em></h1><p className="hero-description">Find home-service providers, choose a time that works for you, and keep every booking in one simple place.</p>
        <div className="hero-actions"><Link href="/register" className="design-button">Make yourself at home <span aria-hidden="true">↗</span></Link><Link href="/services" className="outline-button">Explore services <span aria-hidden="true">→</span></Link></div>
        <div className="hero-assurances"><span><span aria-hidden="true">✓</span> Clear service prices</span><span><span aria-hidden="true">✓</span> Your schedule, your choice</span></div>
        <p className="demo-disclosure">Portfolio demo · Fictional providers and simulated verification checks. No payments.</p>
      </div>
      <div className="hero-art"><div className="art-caption"><span className="art-dot" aria-hidden="true"/> HOME, WITH A LITTLE HELP.</div><HomeIllustration/><div className="art-note"><span className="note-icon" aria-hidden="true">✓</span><div><strong>Good help starts here.</strong><span>Browse. Choose. Request.</span></div><span className="note-arrow" aria-hidden="true">↗</span></div></div>
    </section>
    <section className="service-discovery" aria-labelledby="service-heading"><div className="section-heading"><div><p className="eyebrow">FOR LIFE’S EVERYDAY TASKS</p><h2 id="service-heading">What can we help with?</h2></div><Link href="/services" className="text-link">Explore all services <span aria-hidden="true">→</span></Link></div><div className="discovery-grid">{categories.map(c=><Link key={c.name} href={"/services?q="+c.query} className="discovery-card"><span className="category-symbol" aria-hidden="true">{c.symbol}</span><h3>{c.name}</h3><p>{c.description}</p><span className="card-arrow" aria-hidden="true">↗</span></Link>)}</div><p className="section-fineprint">Explore these service types. Available demo listings depend on what providers publish.</p></section>
    <section id="how-it-works" className="how-section"><div><p className="eyebrow">LESS FRICTION, FROM THE START</p><h2>A simpler way<br/>to get things done.</h2><p>From finding a provider to following your booking, know what happens next.</p></div><ol className="how-steps"><li><span className="step-number">01</span><div><h3>Find your kind of help</h3><p>Explore services, prices, and providers’ precise simulated verification badges.</p></div></li><li><span className="step-number">02</span><div><h3>Choose your place and time</h3><p>Verify your email, add a fictional property, and select an available time.</p></div></li><li><span className="step-number">03</span><div><h3>Keep everything in view</h3><p>Send your request, wait for provider confirmation, and track updates from your dashboard.</p></div></li></ol></section>
    <section className="provider-invitation"><div><p className="eyebrow">GREAT AT WHAT YOU DO?</p><h2>Your skills.<br/>Their next fresh start.</h2><p>Create a provider account, prepare your profile, and submit fictional evidence for review before publishing services.</p><Link href="/register" className="light-button">Become a provider <span aria-hidden="true">↗</span></Link></div><div className="provider-art" aria-hidden="true"><span className="provider-art-ring"/><span className="provider-art-star">✧</span><span className="provider-art-caption">GOOD WORK.<br/>NEW POSSIBILITIES.</span></div></section>
    <footer className="market-footer"><div><strong>Verified Service Marketplace</strong><p>Small tasks. A little more room for life.</p></div><nav aria-label="Footer navigation"><Link href="/services">Services</Link><Link href="/providers">Providers</Link><Link href="/account">Dashboard</Link></nav><div className="footer-status"><span className={connected?"connection-dot":"connection-dot offline"} aria-hidden="true"/>{connected?"Demo API connected":"Demo API unavailable"}<p>Fictional demo data only.</p></div></footer>
  </main>;
}
