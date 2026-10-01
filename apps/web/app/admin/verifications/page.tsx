"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { authRequest, type UserSummary } from "../../../lib/auth-client";
import { verificationRequest, downloadEvidence, type Application } from "../../../lib/verification-client";

export default function AdminVerificationPage() {
    const [user, setUser] = useState<UserSummary | null>(null);
    const [items, setItems] = useState<Application[]>([]);
    const [application, setApplication] = useState<Application | null>(null);
    const [etag, setEtag] = useState("");
    const [filter, setFilter] = useState("");
    const [page, setPage] = useState(1); const [total, setTotal] = useState(0);
    const [decision, setDecision] = useState("AdditionalInformationRequired");
    const [busy, setBusy] = useState(false); const [message, setMessage] = useState("");
    const reviewer = user?.capabilities.includes("Reviewer");
    const claimed = application?.status === "UnderReview" && application.reviewStartedBy === user?.id && new Date(application.reviewLeaseExpiresAt ?? 0).getTime() > Date.now();
    useEffect(() => { authRequest("users/me").then(setUser).catch(() => setMessage("Sign in with a provisioned reviewer account.")); }, []);
    useEffect(() => {
        if (!reviewer) return;
        let cancelled = false; setBusy(true); setApplication(null); setEtag("");
        verificationRequest("admin/verifications?page=" + page + (filter ? "&status=" + filter : ""))
            .then(result => { if (!cancelled) { setItems(result.data.items); setTotal(result.data.totalCount); } })
            .catch(error => { if (!cancelled) setMessage(error.message); })
            .finally(() => { if (!cancelled) setBusy(false); });
        return () => { cancelled = true; };
    }, [reviewer, filter, page]);
    async function action(operation: () => Promise<void>) {
        setBusy(true); setMessage("");
        try { await operation(); } catch (error) { setMessage(error instanceof Error ? error.message : "Review request failed."); } finally { setBusy(false); }
    }
    async function load(id: string) {
        const result = await verificationRequest("admin/verifications/" + id); setApplication(result.data); setEtag(result.etag);
    }
    async function decide(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault(); if (!application) return;
        const form = event.currentTarget; const values = new FormData(form);
        const checks = decision === "Approved" ? values.getAll("checks").map(type => {
            const expiry = values.get("expiry-" + type)?.toString();
            return { type, expiresAt: expiry ? new Date(expiry).toISOString() : null };
        }) : [];
        if (!window.confirm("Record " + decision + "? This decision and its audit history cannot be edited.")) return;
        await action(async () => {
            const result = await verificationRequest("admin/verifications/" + application.id + "/decisions", "POST", { decision, reason: values.get("reason"), checks }, etag);
            setApplication(result.data); setEtag(result.etag); form.reset();
            setMessage("Decision recorded. Provider notifications were saved and owner email queued.");
        });
    }
    return <main className="auth-page"><Link href="/account">Your account</Link><h1>Verification review</h1>
        <p>Fictional evidence only. Approvals record simulated checks; they do not publish a profile or represent a real certification.</p>
        {!reviewer ? <p>A verified, server-provisioned Reviewer account is required.</p> : <>
            <label htmlFor="status">Queue status</label><select id="status" value={filter} disabled={busy} onChange={event => { setFilter(event.target.value); setPage(1); }}>{["", "Submitted", "UnderReview", "Approved", "Rejected", "AdditionalInformationRequired"].map(status => <option value={status} key={status}>{status || "Pending review"}</option>)}</select>
            <p>Page {page} · {total} applications</p>
            {items.length === 0 ? <p>No matching applications.</p> : <ul>{items.map(item => <li key={item.id}>{item.status} · submitted {item.submittedAt ? new Date(item.submittedAt).toLocaleString() : "—"} · {item.id.slice(0, 8)} <button disabled={busy} onClick={() => action(() => load(item.id))}>Inspect</button></li>)}</ul>}
            <button disabled={busy || page === 1} onClick={() => setPage(page - 1)}>Previous</button> <button disabled={busy || page * 20 >= total} onClick={() => setPage(page + 1)}>Next</button>
            {application && <section><h2>{application.submittedSnapshot?.displayName} · {application.status}</h2>
                <p>{application.submittedSnapshot?.providerType} · {application.submittedSnapshot?.serviceArea}</p><p>{application.submittedSnapshot?.biography}</p>
                <p>Submission {application.submissionVersion} · required checks: {application.requiredEvidence.join(", ")}</p>
                <h3>Current private evidence</h3>{application.documents.length === 0 ? <p>No available evidence. Do not approve expired documents.</p> : <ul>{application.documents.map(document => <li key={document.id}>{document.evidenceType} · {document.fileName}<button disabled={busy} onClick={() => action(() => downloadEvidence(document))}>Download for review</button></li>)}</ul>}
                {["Submitted", "UnderReview"].includes(application.status) && !claimed && <button disabled={busy} onClick={() => action(async () => {
                    const result = await verificationRequest("admin/verifications/" + application.id + "/start-review", "POST", {}, etag);
                    setApplication(result.data); setEtag(result.etag); setMessage("Review claimed for 30 minutes.");
                })}>Claim or reclaim review</button>}
                {claimed && <><p>You hold this review until {new Date(application.reviewLeaseExpiresAt!).toLocaleString()}.</p><form onSubmit={decide}><fieldset disabled={busy} className="profile-fields">
                    <label htmlFor="decision">Decision <span className="required-marker" aria-hidden="true">*</span></label><select id="decision" required value={decision} onChange={event => setDecision(event.target.value)}><option>AdditionalInformationRequired</option><option>Rejected</option><option>Approved</option></select>
                    <label htmlFor="reason">Provider-facing reason{decision !== "Approved" && <span className="required-marker" aria-hidden="true"> *</span>}</label><textarea id="reason" name="reason" maxLength={2000} required={decision !== "Approved"} rows={4} />
                    {decision === "Approved" && <fieldset><legend>Explicit simulated checks</legend>{application.requiredEvidence.map(type => <div key={type}><label><input type="checkbox" name="checks" value={type} required /> I reviewed fictional {type} evidence. <span className="required-marker" aria-hidden="true">*</span></label><label htmlFor={"expiry-" + type}>Optional expiry (your local time; defaults to evidence retention)</label><input id={"expiry-" + type} name={"expiry-" + type} type="datetime-local" /></div>)}</fieldset>}
                    <button>Record decision</button>
                </fieldset></form></>}
                <h3>Earlier decisions</h3>{application.decisions.map(item => <p key={item.id}>{item.decision}: {item.reason}</p>)}
                <button disabled={busy} onClick={() => action(() => load(application.id))}>Reload latest review</button>
            </section>}
        </>}
        <p role="status" aria-live="polite">{busy ? "Working…" : message}</p>
    </main>;
}
