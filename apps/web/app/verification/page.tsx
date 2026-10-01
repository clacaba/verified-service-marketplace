"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { authRequest, type UserSummary } from "../../lib/auth-client";
import { verificationRequest, downloadEvidence, type Application, type EvidenceDocument } from "../../lib/verification-client";

export default function VerificationPage() {
    const [organizationId, setOrganizationId] = useState("");
    const [user, setUser] = useState<UserSummary | null>(null);
    const [applications, setApplications] = useState<Application[]>([]);
    const [application, setApplication] = useState<Application | null>(null);
    const [etag, setEtag] = useState("");
    const [busy, setBusy] = useState(true);
    const [message, setMessage] = useState("");
    const [page, setPage] = useState(1); const [total, setTotal] = useState(0);
    const owner = user?.memberships.some(member => member.organizationId === organizationId && member.permissions.includes("verification:write"));
    const base = "organizations/" + organizationId + "/verifications";
    async function select(id: string, route = base) {
        const result = await verificationRequest(route + "/" + id);
        setApplication(result.data); setEtag(result.etag);
    }
    async function refresh(preferred?: string) {
        const result = await verificationRequest(base + "?page=" + page);
        setApplications(result.data.items); setTotal(result.data.totalCount);
        const id = preferred ?? application?.id ?? result.data.items[0]?.id;
        if (id) await select(id);
    }
    useEffect(() => {
        let cancelled = false;
        const id = new URLSearchParams(window.location.search).get("organizationId") ?? "";
        setOrganizationId(id);
        if (!id) { setMessage("Choose an organization first."); setBusy(false); return; }
        const route = "organizations/" + id + "/verifications";
        Promise.all([authRequest("users/me"), verificationRequest(route + "?page=1")])
            .then(async ([current, list]) => {
                if (cancelled) return; setUser(current); setApplications(list.data.items); setTotal(list.data.totalCount);
                if (list.data.items[0]) {
                    const result = await verificationRequest(route + "/" + list.data.items[0].id);
                    if (!cancelled) { setApplication(result.data); setEtag(result.etag); }
                }
            }).catch(error => { if (!cancelled) setMessage(error.message); }).finally(() => { if (!cancelled) setBusy(false); });
        return () => { cancelled = true; };
    }, []);
    async function action(operation: () => Promise<void>) {
        setBusy(true); setMessage("");
        try { await operation(); }
        catch (error) { setMessage(error instanceof Error ? error.message : "Request failed."); }
        finally { setBusy(false); }
    }
    async function upload(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault(); if (!application) return;
        const form = event.currentTarget; const payload = new FormData(form); const file = payload.get("file");
        if (!(file instanceof File) || file.size === 0 || file.size > 10 * 1024 * 1024) { setMessage("Choose a nonempty fictional PDF/JPEG/PNG up to 10 MiB."); return; }
        await action(async () => {
            await verificationRequest(base + "/" + application.id + "/documents", "POST", payload, etag);
            await refresh(application.id); form.reset(); setMessage("Private evidence uploaded and scanned.");
        });
    }
    async function remove(document: EvidenceDocument) {
        if (!application || !window.confirm("Remove this draft evidence? Its private file will be queued for deletion.")) return;
        await action(async () => {
            await verificationRequest(base + "/" + application.id + "/documents/" + document.id, "DELETE", {}, etag);
            await refresh(application.id); setMessage("Draft document removed.");
        });
    }
    return <main className="auth-page">
        <Link href="/organizations">Organizations</Link><h1>Verification evidence</h1>
        <p>Portfolio demo only: upload fictional documents, never real identity or business records. These evidence rules are simulated, not legal requirements.</p>
        {organizationId && <Link href={"/provider-profile?organizationId=" + organizationId}>Complete provider profile</Link>}
        {busy && <p>Working…</p>}
        {!user && !busy && <Link href="/login">Sign in</Link>}
        {user && <>
            {applications.length === 0 && <p>No verification application yet.</p>}
            {owner && !application && <button disabled={busy} onClick={() => action(async () => {
                const result = await verificationRequest(base, "POST", {}); await refresh(result.data.id); setMessage("Private draft created.");
            })}>Create verification draft</button>}
            {applications.length > 0 && <><label htmlFor="application">Application history</label><select id="application" disabled={busy} value={application?.id ?? ""} onChange={event => action(() => select(event.target.value))}>{applications.map(item => <option value={item.id} key={item.id}>{item.status} · {item.id.slice(0, 8)}</option>)}</select>
                <p>Page {page} · {total} applications</p>
                <button disabled={busy || page === 1} onClick={() => action(async () => { const next = page - 1; const result = await verificationRequest(base + "?page=" + next); setPage(next); setApplications(result.data.items); if (result.data.items[0]) await select(result.data.items[0].id); })}>Previous</button>
                <button disabled={busy || page * 20 >= total} onClick={() => action(async () => { const next = page + 1; const result = await verificationRequest(base + "?page=" + next); setPage(next); setApplications(result.data.items); if (result.data.items[0]) await select(result.data.items[0].id); })}>Next</button>
            </>}
            {application && <section><h2>{application.status} application</h2>
                <p>Required evidence: {application.requiredEvidence.join(", ")}.</p>
                <ul>{application.requiredEvidence.map(type => <li key={type}>{type}: {application.documents.some(document => document.evidenceType === type) ? "Uploaded" : "Missing"}</li>)}</ul>
                <h3>Private documents</h3>
                {application.documents.length === 0 ? <p>No current documents.</p> : <ul>{application.documents.map(document => <li key={document.id}>{document.fileName} · {document.evidenceType} · {Math.ceil(document.size / 1024)} KiB · scanned clean
                    <button disabled={busy} onClick={() => action(() => downloadEvidence(document))}>Download privately</button>
                    {owner && application.status === "Draft" && <button disabled={busy} onClick={() => remove(document)}>Remove</button>}
                </li>)}</ul>}
                {owner && application.status === "Draft" && <><form onSubmit={upload}>
                    <label htmlFor="evidenceType">Evidence type</label><select id="evidenceType" name="evidenceType" disabled={busy}>{["Identity", "BusinessRegistration", "TradeQualification"].map(type => <option key={type}>{type}</option>)}</select>
                    <label htmlFor="evidenceFile">Fictional evidence file</label><input id="evidenceFile" name="file" type="file" accept=".pdf,.jpg,.jpeg,.png" required disabled={busy} />
                    <label><input type="checkbox" name="demoAcknowledged" value="true" required disabled={busy} /> I confirm this is fictional demonstration evidence.</label>
                    <button disabled={busy || !etag}>Upload private evidence</button>
                </form>
                {!user.emailConfirmed && <p>Verify your email before submitting.</p>}
                <button disabled={busy || !user.emailConfirmed || !etag || application.requiredEvidence.some(type => !application.documents.some(document => document.evidenceType === type))} onClick={() => {
                    if (!window.confirm("Submit this profile and evidence snapshot? Submitted evidence cannot be edited.")) return;
                    action(async () => { await verificationRequest(base + "/" + application.id + "/submit", "POST", {}, etag); await refresh(application.id); setMessage("Submitted. The evidence snapshot is now locked."); });
                }}>Submit for review</button></>}
                {application.status !== "Draft" && <p>Submitted evidence remains locked. Your latest status is {application.status}.</p>}
                {application.parentApplicationId && <p>Correction draft linked to application {application.parentApplicationId.slice(0, 8)} · revision {application.revisionNumber}.</p>}
                <h3>Decision history</h3>
                {application.decisions.length === 0 ? <p>No review decision yet.</p> : <ul>{application.decisions.map(decision => <li key={decision.id}>{decision.decision} · {new Date(decision.decidedAt).toLocaleString()}<p>{decision.reason}</p>{decision.checks.map(check => <p key={check.type}>Simulated {check.type} · {check.revokedAt ? "Revoked after profile changes" : "Expires " + new Date(check.expiresAt).toLocaleString()}</p>)}</li>)}</ul>}
                {owner && application.canCreateRevision && <button disabled={busy} onClick={() => action(async () => { const result = await verificationRequest(base + "/" + application.id + "/revisions", "POST", {}, etag); await refresh(result.data.id); setMessage("New draft created. Earlier evidence and decisions are preserved. Upload fresh fictional evidence."); })}>{application.status === "Approved" ? "Create renewal draft" : "Create corrected draft"}</button>}
                {!owner && <p>Your Staff role permits reading and private downloads, not uploading or submitting.</p>}
                <button disabled={busy} onClick={() => action(() => refresh(application.id))}>Reload latest application</button>
            </section>}
        </>}
        <p role="status" aria-live="polite">{message}</p>
    </main>;
}
