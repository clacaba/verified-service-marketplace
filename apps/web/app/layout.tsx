import type { Metadata } from "next";
import "./globals.css";
import "./dashboard.css";
import "./visual-design.css";
import "./reviews.css";
export const metadata: Metadata = { title: "Verified Service Marketplace", description: "Verified home-service providers, organized around trust.", referrer: "no-referrer" };
export default function Layout({children}: Readonly<{children: React.ReactNode}>) { return <html lang="en"><body>{children}</body></html>; }
