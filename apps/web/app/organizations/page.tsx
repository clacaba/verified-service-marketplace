"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { authRequest, type UserSummary } from "../../lib/auth-client";

type Member = { userId: string; name: string; email: string; role: string; active: boolean };
export default function Organizations() {
    const [user, setUser] = useState<UserSummary | null>(null);
    const [selected, setSelected] = useState("");
    const [members, setMembers] = useState<Member[]>([]);
    const [message, setMessage] = useState("");
    const [busy, setBusy] = useState(false);
    const [loadingMembers, setLoadingMembers] = useState(false);
    const [page, setPage] = useState(1);
    const [total, setTotal] = useState(0);
    const membership = user?.memberships.find(item => item.organizationId === selected);
    const owner = membership?.role === "Owner";

    async function refresh(preferred?: string) {
        const current: UserSummary = await authRequest("users/me");
        setUser(current);
        setSelected(previous => preferred ?? (current.memberships.some(item => item.organizationId === previous) ? previous : current.memberships[0]?.organizationId ?? ""));
    }
    useEffect(() => { refresh().catch(() => setMessage("Please sign in to manage organizations.")); }, []);
    useEffect(() => {
        let cancelled = false;
        setMembers([]);
        setTotal(0);
        if (!selected || !owner) return;
        setLoadingMembers(true);
        authRequest("organizations/" + selected + "/members?page=" + page + "&pageSize=20")
            .then(result => { if (!cancelled) { setMembers(result.items); setTotal(result.totalCount); } })
            .catch(error => { if (!cancelled) setMessage(error.message); })
            .finally(() => { if (!cancelled) setLoadingMembers(false); });
        return () => { cancelled = true; };
    }, [selected, owner, page]);

    async function create(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault(); setBusy(true); setMessage("");
        const form = event.currentTarget; const fields = new FormData(form);
        try { const organization = await authRequest("organizations", { name: fields.get("name"), providerType: fields.get("providerType") }); await refresh(organization.id); setPage(1); form.reset(); setMessage("Organization created. You are its owner."); }
        catch (error) { setMessage(error instanceof Error ? error.message : "Unable to create organization."); }
        finally { setBusy(false); }
    }
    async function invite(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault(); setBusy(true); setMessage("");
        const form = event.currentTarget;
        try { await authRequest("organizations/" + selected + "/invitations", { email: new FormData(form).get("email"), role: "Staff" }); form.reset(); setMessage("Invitation queued for email delivery. It expires in seven days."); }
        catch (error) { setMessage(error instanceof Error ? error.message : "Unable to invite staff."); }
        finally { setBusy(false); }
    }
    async function deactivate(member: Member) {
        if (!window.confirm("Deactivate " + member.name + "'s access to this organization?")) return;
        setBusy(true); setMessage("");
        try { await authRequest("organizations/" + selected + "/members/" + member.userId, {}, "DELETE"); setMembers(previous => previous.map(item => item.userId === member.userId ? { ...item, active: false } : item)); setMessage("Staff access deactivated."); }
        catch (error) { setMessage(error instanceof Error ? error.message : "Unable to deactivate staff."); }
        finally { setBusy(false); }
    }

    return <main className="auth-page">
        <Link href="/account">Your account</Link><h1>Organizations</h1>
        {!user ? <><p>{message || "Loading your organizations…"}</p><Link href="/login">Sign in</Link></> : <>
            {user.capabilities.includes("Provider") && <section><h2>Create a provider organization</h2><form onSubmit={create}>
                <label htmlFor="organizationName">Organization name</label><input id="organizationName" name="name" required maxLength={150} />
                <label htmlFor="providerType">Provider type</label><select id="providerType" name="providerType"><option value="Individual">Independent provider</option><option value="Business">Provider company</option></select>
                <button disabled={busy}>Create organization</button>
            </form></section>}
            {user.memberships.length === 0 ? <p>You do not belong to any organizations yet. Providers can create one; invited staff should follow their email invitation after verifying their email.</p> : <section>
                <label htmlFor="activeOrganization">Active organization</label><select id="activeOrganization" value={selected} onChange={event => { setSelected(event.target.value); setPage(1); setMessage(""); }}>{user.memberships.map(item => <option key={item.organizationId} value={item.organizationId}>{item.name} ({item.role})</option>)}</select>
                <p>Your role: {membership?.role}. Permissions: {membership?.permissions.join(", ")}.</p>
                {owner ? <>
                    <h2>Invite staff</h2><form onSubmit={invite}><label htmlFor="staffEmail">Staff email</label><input id="staffEmail" name="email" type="email" required maxLength={254} /><p>Staff can read organization information; owner management actions remain restricted.</p><button disabled={busy}>Send invitation</button></form>
                    <h2>Members</h2>{loadingMembers ? <p>Loading members…</p> : <ul>{members.map(member => <li key={member.userId}><span>{member.name} — {member.email} · {member.role} · {member.active ? "Active" : "Inactive"}</span>{member.role !== "Owner" && member.active && <button disabled={busy} onClick={() => deactivate(member)}>Deactivate</button>}</li>)}</ul>}
                    <p>Page {page} · {total} members</p><button disabled={page === 1 || loadingMembers} onClick={() => setPage(page - 1)}>Previous</button> <button disabled={page * 20 >= total || loadingMembers} onClick={() => setPage(page + 1)}>Next</button>
                </> : <p>Your staff membership does not permit inviting or removing members.</p>}
            </section>}
        </>}
        <p role="status" aria-live="polite">{user && message}</p>
    </main>;
}
