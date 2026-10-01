"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { authRequest } from "../../lib/auth-client";
import { profileRequest, setPublication, ProfileError, type Profile } from "../../lib/profile-client";

type Category = { id: string; name: string };
export default function ProviderProfilePage() {
    const [organizationId, setOrganizationId] = useState("");
    const [profile, setProfile] = useState<Profile | null>(null);
    const [etag, setEtag] = useState("");
    const [categories, setCategories] = useState<Category[]>([]);
    const [editable, setEditable] = useState(false);
    const [busy, setBusy] = useState(true);
    const [message, setMessage] = useState("");
    const [conflict, setConflict] = useState(false);
    const [dirty, setDirty] = useState(false);

    useEffect(() => {
        let cancelled = false;
        const id = new URLSearchParams(window.location.search).get("organizationId") ?? "";
        setOrganizationId(id);
        if (!id) { setMessage("Choose an organization first."); setBusy(false); return; }
        Promise.all([authRequest("users/me"), authRequest("categories?pageSize=100"), profileRequest(id)])
            .then(([user, list, result]) => {
                if (cancelled) return;
                setEditable(user.memberships.some((member: { organizationId: string; permissions: string[] }) => member.organizationId === id && member.permissions.includes("profile:write")));
                setCategories(list.items); setProfile(result.profile); setEtag(result.etag);
            })
            .catch(error => { if (!cancelled) setMessage(error.message); })
            .finally(() => { if (!cancelled) setBusy(false); });
        return () => { cancelled = true; };
    }, []);
    useEffect(() => {
        const warn = (event: BeforeUnloadEvent) => { if (dirty) event.preventDefault(); };
        window.addEventListener("beforeunload", warn);
        return () => window.removeEventListener("beforeunload", warn);
    }, [dirty]);
    function change(update: Partial<Profile>) { setProfile(previous => previous ? { ...previous, ...update } : previous); setDirty(true); setMessage(""); }
    async function save(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault(); if (!profile || conflict) return;
        setBusy(true); setMessage("");
        try { const result = await profileRequest(organizationId, profile, etag); setProfile(result.profile); setEtag(result.etag); setDirty(false); setMessage("Profile saved. Material changes remove public visibility and require renewed verification."); }
        catch (error) { setMessage(error instanceof Error ? error.message : "Save failed."); if (error instanceof ProfileError && error.status === 412) setConflict(true); }
        finally { setBusy(false); }
    }
    async function publish(published: boolean) {
        if (dirty || busy || !profile || !editable || conflict) return;
        if (published && !window.confirm("Publish your saved name, biography, area, categories, and simulated badges publicly?")) return;
        setBusy(true); setMessage("");
        try {
            await setPublication(organizationId, published, etag);
            const result = await profileRequest(organizationId); setProfile(result.profile); setEtag(result.etag);
            setMessage(published ? "Publication enabled. Public visibility still requires current verification." : "Profile unpublished.");
        } catch (error) {
            setMessage(error instanceof Error ? error.message : "Publication failed.");
            if (error instanceof ProfileError && error.status === 412) setConflict(true);
        } finally { setBusy(false); }
    }
    async function reload() {
        if (dirty && !window.confirm("Reload and discard your unsaved edits?")) return;
        setBusy(true);
        try { const result = await profileRequest(organizationId); setProfile(result.profile); setEtag(result.etag); setDirty(false); setConflict(false); setMessage("Latest draft loaded."); }
        catch (error) { setMessage(error instanceof Error ? error.message : "Reload failed."); }
        finally { setBusy(false); }
    }
    return <main className="auth-page">
        <Link href="/organizations" onClick={event => { if (dirty && !window.confirm("Leave without saving your edits?")) event.preventDefault(); }}>Organizations</Link>
        <h1>Provider profile draft</h1>
        <p>Saving does not verify your profile. Material changes clear publication and revoke existing checks.</p>
        {busy && !profile && <p>Loading profile…</p>}
        {profile && <><p>Status: Draft · {dirty ? "Unsaved changes" : "Saved"} · {editable ? "Owner editing" : "Staff read-only"}</p>
            <form onSubmit={save}><fieldset disabled={!editable || busy || conflict} className="profile-fields">
                <label htmlFor="displayName">Display name <span className="required-marker" aria-hidden="true">*</span></label><input id="displayName" required maxLength={150} value={profile.displayName} onChange={event => change({ displayName: event.target.value })} />
                <label htmlFor="providerType">Provider type <span className="required-marker" aria-hidden="true">*</span></label><select id="providerType" required value={profile.providerType} onChange={event => change({ providerType: event.target.value as Profile["providerType"] })}><option value="Individual">Independent provider</option><option value="Business">Provider company</option></select>
                <label htmlFor="biography">Biography</label><textarea id="biography" rows={6} maxLength={4000} value={profile.biography} onChange={event => change({ biography: event.target.value })} />
                <label htmlFor="serviceArea">Service area</label><input id="serviceArea" maxLength={250} value={profile.serviceArea} onChange={event => change({ serviceArea: event.target.value })} />
                <fieldset><legend>Service categories</legend>{categories.map(category => <label className="category-choice" key={category.id}><input type="checkbox" checked={profile.categoryIds.includes(category.id)} onChange={event => change({ categoryIds: event.target.checked ? [...profile.categoryIds, category.id] : profile.categoryIds.filter(id => id !== category.id) })} /> {category.name}</label>)}</fieldset>
                <small>Biography, area, and categories may be left incomplete while drafting. Images will be added with the upload feature.</small>
                {editable && <button disabled={!dirty || !etag}>Save draft</button>}
            </fieldset></form>
            <p>{profile.readyForVerification ? "Saved profile has its basic fields complete. Submit evidence from your organization verification page." : "Complete biography, service area, and at least one category before evidence submission."}</p>
            <section aria-label="Public profile publication">
                <h2>Public profile</h2>
                <p>{profile.publishedAt ? "Publication enabled; the public page is visible only while all required checks remain current." : "Not published. Approval alone does not publish your information."}</p>
                {editable && <button type="button" disabled={busy || dirty || conflict || !etag} onClick={() => publish(!profile.publishedAt)}>{profile.publishedAt ? "Unpublish profile" : "Publish approved profile"}</button>}
                {profile.publishedAt && <p><Link href={"/providers/" + profile.id} prefetch={false}>Open public profile</Link></p>}
                <p>Demo checks are simulated, not real identity or licensing certification.</p>
            </section>
            <button type="button" disabled={busy} onClick={reload}>{conflict ? "Reload latest draft (discard edits)" : "Reload saved draft"}</button>
        </>}
        <p role="status" aria-live="polite">{message}</p>
    </main>;
}
