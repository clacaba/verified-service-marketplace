"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { authRequest } from "../../lib/auth-client";
import { catalogRequest, CatalogError, price, type Listing, type ListingInput } from "../../lib/catalog-client";
const blank: ListingInput = {title: "", description: "", categoryId: "", priceMinor: 10000, currency: "PHP", durationMinutes: 60, serviceArea: ""};
export default function Catalog() {
    const [org, setOrg] = useState(""); const [owner, setOwner] = useState(false);
    const [items, setItems] = useState<Listing[]>([]); const [total, setTotal] = useState(0); const [page, setPage] = useState(1);
    const [categories, setCategories] = useState<{id: string; name: string}[]>([]);
    const [input, setInput] = useState<ListingInput>({...blank}); const [selected, setSelected] = useState<Listing | null>(null);
    const [etag, setEtag] = useState(""); const [dirty, setDirty] = useState(false); const [conflict, setConflict] = useState(false);
    const [busy, setBusy] = useState(true); const [message, setMessage] = useState(""); const [ready, setReady] = useState(false);
    const root = "organizations/" + org + "/services";
    async function list(id: string, current: number) {
        const result = await catalogRequest<{items: Listing[]; totalCount: number}>("organizations/" + id + "/services?page=" + current);
        setItems(result.data.items); setTotal(result.data.totalCount);
    }
    useEffect(() => {
        const id = new URLSearchParams(window.location.search).get("organizationId") ?? ""; setOrg(id);
        if (!id) {setMessage("Choose an organization first."); setBusy(false); return;}
        let cancelled = false;
        Promise.all([authRequest("users/me"), authRequest("categories?pageSize=100"), authRequest("organizations/" + id + "/provider-profile"),
            catalogRequest<{items: Listing[]; totalCount: number}>("organizations/" + id + "/services")])
            .then(([user, categoryList, profile, result]) => {
                if (cancelled) return;
                setOwner(user.memberships.some((m: {organizationId: string; permissions: string[]}) => m.organizationId === id && m.permissions.includes("catalog:write")));
                const active = categoryList.items.filter((c: {id: string}) => profile.categoryIds.includes(c.id));
                setCategories(active); setInput({...blank, categoryId: active[0]?.id ?? "", serviceArea: profile.serviceArea});
                setItems(result.data.items); setTotal(result.data.totalCount); setReady(true);
            }).catch(error => {if (!cancelled) setMessage(error.message);}).finally(() => {if (!cancelled) setBusy(false);});
        return () => {cancelled = true;};
    }, []);
    useEffect(() => {const warn = (e: BeforeUnloadEvent) => {if (dirty) e.preventDefault();}; window.addEventListener("beforeunload", warn); return () => window.removeEventListener("beforeunload", warn);}, [dirty]);
    function fail(error: unknown) {setMessage(error instanceof Error ? error.message : "Request failed."); if (error instanceof CatalogError && error.status === 412) setConflict(true);}
    async function open(item: Listing) {
        if (dirty && !window.confirm("Discard your unsaved listing edits?")) return;
        setBusy(true);
        try {const result = await catalogRequest<Listing>(root + "/" + item.id); setSelected(result.data); setInput(result.data); setEtag(result.etag); setDirty(false); setConflict(false); setMessage("");}
        catch (error) {fail(error);} finally {setBusy(false);}
    }
    function change(patch: Partial<ListingInput>) {setInput(value => ({...value, ...patch})); setDirty(true);}
    async function save(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault(); setBusy(true); setMessage("");
        try {
            const result = await catalogRequest<Listing>(root + (selected ? "/" + selected.id : ""), selected ? "PUT" : "POST", {
                title: input.title, description: input.description, categoryId: input.categoryId, priceMinor: input.priceMinor, currency: input.currency, durationMinutes: input.durationMinutes, serviceArea: input.serviceArea
            }, selected ? etag : undefined);
            setSelected(result.data); setInput(result.data); setEtag(result.etag); setDirty(false); setConflict(false);
            await list(org, page); setMessage("Saved as a private draft. Publish separately when ready.");
        } catch (error) {fail(error);} finally {setBusy(false);}
    }
    async function action(action: "publication" | "archive", published?: boolean) {
        if (!selected || dirty || conflict) return;
        if (!window.confirm(action === "archive" ? "Archive this listing permanently? It cannot be edited or republished." : published ? "Publish this saved offer publicly?" : "Remove this offer from public discovery?")) return;
        setBusy(true);
        try {
            const result = await catalogRequest<Listing>(root + "/" + selected.id + "/" + action, "POST", action === "publication" ? {published} : {}, etag);
            setSelected(result.data); setInput(result.data); setEtag(result.etag); await list(org, page); setMessage("Listing status: " + result.data.status);
        } catch (error) {fail(error);} finally {setBusy(false);}
    }
    async function nextPage(value: number) {setBusy(true); try {await list(org, value); setPage(value);} catch(error) {fail(error);} finally {setBusy(false);}}
    return <main className="auth-page"><Link href="/organizations" onClick={e => {if(dirty && !window.confirm("Leave without saving?")) e.preventDefault();}}>Organizations</Link><h1>Service catalog</h1>
        <p>Fixed-price PHP offers. Owner management; Staff read-only. Publication requires a published provider profile with current checks. Images and booking are not available yet.</p>
        {busy && <p role="status">Loading or saving…</p>}
        {ready && <><section><h2>Your listings</h2>{items.length === 0 ? <p>No listings on this page.</p> : <ul>{items.map(item => <li key={item.id}>
            <button disabled={busy} onClick={() => open(item)}>{item.title}</button> · {item.status} · {price(item.priceMinor)}
        </li>)}</ul>}
        <p>Page {page} · {total} listings</p><button disabled={busy || page === 1} onClick={() => nextPage(page - 1)}>Previous</button>{" "}<button disabled={busy || page * 20 >= total} onClick={() => nextPage(page + 1)}>Next</button>
        {owner && <button disabled={busy} onClick={() => {if (dirty && !window.confirm("Discard unsaved edits?")) return; setSelected(null); setInput({...blank, categoryId: categories[0]?.id ?? ""}); setDirty(false); setConflict(false); setEtag(""); setMessage("");}}>New listing</button>}</section>
        {(owner || selected) && <section><h2>{selected ? selected.title : "Create a listing"}</h2><p>{selected ? "Status: " + selected.status : "New private draft"} · {dirty ? "Unsaved changes" : "Saved"}</p>
        <form onSubmit={save}><fieldset disabled={busy || !owner || conflict || selected?.status === "Archived"}>
            <label htmlFor="title">Title</label><input id="title" required maxLength={150} value={input.title} onChange={e => change({title: e.target.value})}/>
            <label htmlFor="description">Description</label><textarea id="description" required maxLength={4000} rows={5} value={input.description} onChange={e => change({description: e.target.value})}/>
            <label htmlFor="category">Category</label><select id="category" required value={input.categoryId} onChange={e => change({categoryId: e.target.value})}><option value="">Choose a provider category</option>{categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select>
            <label htmlFor="price">Price in centavos (100 = PHP 1)</label><input id="price" type="number" required min={1} max={100000000} step={1} value={input.priceMinor} onChange={e => change({priceMinor: Number(e.target.value)})}/><small>{price(input.priceMinor)}</small>
            <label htmlFor="duration">Duration in minutes</label><input id="duration" type="number" required min={15} max={1440} step={15} value={input.durationMinutes} onChange={e => change({durationMinutes: Number(e.target.value)})}/>
            <label htmlFor="area">Service area</label><input id="area" required maxLength={250} value={input.serviceArea} onChange={e => change({serviceArea: e.target.value})}/>
            {owner && <button disabled={!!selected && !dirty}>Save draft</button>}
        </fieldset></form>
        {selected && <><button disabled={busy} onClick={() => open(selected)}>Reload saved listing</button>
        {owner && selected.status !== "Archived" && <><button disabled={busy || dirty || conflict} onClick={() => action("publication", selected.status !== "Published")}>{selected.status === "Published" ? "Unpublish" : "Publish"}</button>{" "}<button disabled={busy || dirty || conflict} onClick={() => action("archive")}>Archive</button></>}
        {selected.status === "Published" && <p><Link href={"/services/" + selected.id} prefetch={false}>Open public listing</Link> (hidden if provider eligibility lapses)</p>}</>}
        </section>}</>}
        <p role="status" aria-live="polite">{message}</p>
    </main>;
}
