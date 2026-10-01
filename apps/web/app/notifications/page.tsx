"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { verificationRequest } from "../../lib/verification-client";
type Note = { id: string; organizationId: string; message: string; createdAt: string; read: boolean };
export default function NotificationsPage() {
    const [items, setItems] = useState<Note[]>([]); const [page, setPage] = useState(1); const [total, setTotal] = useState(0);
    const [busy, setBusy] = useState(false); const [message, setMessage] = useState("");
    useEffect(() => {
        let cancelled = false; setBusy(true);
        verificationRequest("notifications?page=" + page).then(result => { if (!cancelled) { setItems(result.data.items); setTotal(result.data.totalCount); } }).catch(error => { if (!cancelled) setMessage(error.message); }).finally(() => { if (!cancelled) setBusy(false); });
        return () => { cancelled = true; };
    }, [page]);
    async function markRead(id: string) {
        setBusy(true); setMessage("");
        try { await verificationRequest("notifications/" + id, "PATCH", { read: true }); setItems(previous => previous.map(item => item.id === id ? { ...item, read: true } : item)); }
        catch (error) { setMessage(error instanceof Error ? error.message : "Unable to mark read."); }
        finally { setBusy(false); }
    }
    return <main className="auth-page"><Link href="/account">Your account</Link><h1>Your notifications</h1>
        {items.length === 0 ? <p>No notifications.</p> : <ul>{items.map(item => <li key={item.id}><p>{item.message}</p><small>{new Date(item.createdAt).toLocaleString()} · {item.read ? "Read" : "Unread"}</small> <Link href={"/verification?organizationId=" + item.organizationId}>View application</Link>{!item.read && <button disabled={busy} onClick={() => markRead(item.id)}>Mark read</button>}</li>)}</ul>}
        <p>Page {page} · {total} notifications</p><button disabled={busy || page === 1} onClick={() => setPage(page - 1)}>Previous</button> <button disabled={busy || page * 20 >= total} onClick={() => setPage(page + 1)}>Next</button>
        <p role="status" aria-live="polite">{busy ? "Loading…" : message}</p>
    </main>;
}
