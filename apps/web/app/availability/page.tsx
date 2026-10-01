"use client";
import {useEffect, useState, type FormEvent} from "react";
import {catalogRequest} from "../../lib/catalog-client";
type Configuration = {timeZone: string; bufferMinutes: number; minimumNoticeMinutes: number; weekly: {day: number; startMinute: number; endMinute: number}[]; exceptions: {date: string; windows: {startMinute: number; endMinute: number}[]}[]};
type Membership = {organizationId: string; name: string; role: string};
export default function Availability() {
    const [memberships, setMemberships] = useState<Membership[]>([]); const [org, setOrg] = useState(""); const [configuration, setConfiguration] = useState<Configuration | null>(null);
    const [weekly, setWeekly] = useState("[]"); const [exceptions, setExceptions] = useState("[]"); const [etag, setEtag] = useState(""); const [busy, setBusy] = useState(false); const [message, setMessage] = useState("");
    useEffect(() => {catalogRequest<{memberships: Membership[]}>("users/me").then(({data}) => {setMemberships(data.memberships); setOrg(data.memberships[0]?.organizationId ?? "");}).catch(error => setMessage(error.message));}, []);
    async function load(id: string) { const result = await catalogRequest<{configuration: Configuration}>(`organizations/${id}/availability`); setConfiguration(result.data.configuration); setWeekly(JSON.stringify(result.data.configuration.weekly, null, 2)); setExceptions(JSON.stringify(result.data.configuration.exceptions, null, 2)); setEtag(result.etag); }
    useEffect(() => {if (org) {setConfiguration(null); load(org).catch(error => setMessage(error.message));}}, [org]);
    async function save(event: FormEvent) {
        event.preventDefault(); if (!configuration) return; setBusy(true); setMessage("");
        try { const input = {...configuration, weekly: JSON.parse(weekly), exceptions: JSON.parse(exceptions)}; const result = await catalogRequest<{configuration: Configuration}>(`organizations/${org}/availability`, "PUT", input, etag); setConfiguration(result.data.configuration); setEtag(result.etag); setMessage("Availability saved. Existing bookings are unchanged."); }
        catch (error) {setMessage((error as Error).message + " If another request changed availability, reload before saving.");} finally {setBusy(false);}
    }
    const owner = memberships.find(m => m.organizationId === org)?.role === "Owner";
    return <main className="auth-page"><a href="/organizations">Organizations</a><h1>Provider availability</h1><p>One shared capacity per organization. Use separate working windows to represent breaks. Bookings use UTC; working hours use your IANA timezone. Ambiguous DST times and appointments spanning an offset change are unavailable.</p>
        <p role="status">{message}</p><label>Organization<select value={org} onChange={e => {if(window.confirm("Switch organization and discard any unsaved schedule edits?")) setOrg(e.target.value);}}>{memberships.map(m => <option key={m.organizationId} value={m.organizationId}>{m.name} ({m.role})</option>)}</select></label>
        {configuration && <form onSubmit={save}><fieldset disabled={busy || !owner}><label>Timezone <span className="required-marker" aria-hidden="true">*</span><input required value={configuration.timeZone} onChange={e => setConfiguration({...configuration, timeZone: e.target.value})}/></label>
            <label>Buffer before and after each service (minutes) <span className="required-marker" aria-hidden="true">*</span><input type="number" min="0" max="120" required value={configuration.bufferMinutes} onChange={e => setConfiguration({...configuration, bufferMinutes: Number(e.target.value)})}/></label>
            <label>Minimum notice (minutes) <span className="required-marker" aria-hidden="true">*</span><input type="number" min="0" max="10080" required value={configuration.minimumNoticeMinutes} onChange={e => setConfiguration({...configuration, minimumNoticeMinutes: Number(e.target.value)})}/></label>
            <label>Weekly windows (JSON)<textarea rows={12} value={weekly} onChange={e => setWeekly(e.target.value)}/></label>
            <p>Day 0 is Sunday, 1 is Monday. Minutes are measured from midnight: 540 = 09:00, 1020 = 17:00. Example Monday: {`[{"day":1,"startMinute":540,"endMinute":720},{"day":1,"startMinute":780,"endMinute":1020}]`}</p>
            <label>Date exceptions (JSON)<textarea rows={8} value={exceptions} onChange={e => setExceptions(e.target.value)}/></label><p>An exception replaces that date's weekly windows. Close a date with {`[{"date":"2026-12-25","windows":[]}]`}.</p>
            <button>Save availability</button></fieldset></form>}
        {org && <button disabled={busy} onClick={() => {if(window.confirm("Discard unsaved schedule edits and reload?")) load(org).catch(error => setMessage(error.message));}}>Reload latest availability</button>}
        {!owner && <p>Only the organization Owner can change availability. Staff can view it.</p>}<a href="/bookings">Manage bookings</a>
    </main>;
}
