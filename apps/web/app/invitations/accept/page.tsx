"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { authRequest, type UserSummary } from "../../../lib/auth-client";

export default function AcceptInvitation() {
    const [user, setUser] = useState<UserSummary | null>(null);
    const [message, setMessage] = useState("");
    const [loading, setLoading] = useState(true);
    const [busy, setBusy] = useState(false);
    const [done, setDone] = useState(false);
    useEffect(() => { authRequest("users/me").then(setUser).catch(() => setMessage("Sign in with the invited email, then return to this invitation link.")).finally(() => setLoading(false)); }, []);
    async function accept() {
        setBusy(true); setMessage("");
        try {
            const token = new URLSearchParams(window.location.search).get("token");
            if (!token) throw new Error("This invitation link is incomplete.");
            await authRequest("invitations/accept", { token });
            window.history.replaceState(null, "", "/invitations/accept");
            setDone(true); setMessage("Invitation accepted. Your staff membership is active.");
        } catch (error) { setMessage(error instanceof Error ? error.message : "Unable to accept invitation."); }
        finally { setBusy(false); }
    }
    return <main className="auth-page"><h1>Join an organization</h1>
        {loading ? <p>Checking your account…</p> : user ? <><p>Signed in as {user.email}. The invitation must match this verified email.</p>{!user.emailConfirmed && <p>Verify your email from <Link href="/account">your account</Link>, then reopen this invitation.</p>}{!done && <button disabled={busy || !user.emailConfirmed} onClick={accept}>{busy ? "Accepting…" : "Accept staff invitation"}</button>}</> : <><Link href="/login">Sign in</Link> <Link href="/register">Create an account</Link><p>Register using the invited email and verify it before accepting.</p></>}
        <p role="status" aria-live="polite">{message}</p>{done && <Link href="/organizations">View your organizations</Link>}
    </main>;
}
