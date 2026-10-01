"use client";

import { useEffect, useState, type FormEvent } from "react";
import { catalogRequest, CatalogError } from "../../lib/catalog-client";

type Address = { label: string; addressLine1: string; addressLine2: string; city: string; region: string; postalCode: string; countryCode: string };
type Property = Address & { id: string; version: string; archivedAt: string | null };
type PropertyList = { items: Property[]; totalCount: number };
const empty: Address = { label: "", addressLine1: "", addressLine2: "", city: "", region: "", postalCode: "", countryCode: "PH" };
const fields: [keyof Address, string, number, boolean][] = [["label", "Label (for example Home)", 80, true], ["addressLine1", "Street address", 200, true], ["addressLine2", "Apartment / unit (optional)", 200, false], ["city", "City", 100, true], ["region", "Province / region", 100, true], ["postalCode", "Postal code", 20, true], ["countryCode", "Country code (for example PH)", 2, true]];

export default function PropertiesPage() {
    const [items, setItems] = useState<Property[]>([]); const [total, setTotal] = useState(0); const [page, setPage] = useState(1);
    const [form, setForm] = useState<Address>(empty); const [selected, setSelected] = useState<string | null>(null); const [etag, setEtag] = useState("");
    const [busy, setBusy] = useState(false); const [ready, setReady] = useState(false); const [message, setMessage] = useState(""); const [conflict, setConflict] = useState(false);
    const [dirty, setDirty] = useState(false);
    async function refresh(current: number) {
        const result = await catalogRequest<PropertyList>(`properties?page=${current}&pageSize=20`);
        setItems(result.data.items); setTotal(result.data.totalCount); setReady(true);
    }
    useEffect(() => {
        let active = true;
        catalogRequest<PropertyList>(`properties?page=${page}&pageSize=20`).then(({data}) => { if (active) { setItems(data.items); setTotal(data.totalCount); setReady(true); } }).catch(error => { if (active) { setMessage(error instanceof CatalogError && error.status === 401 ? "Sign in to manage your properties." : error.message); setReady(false); } });
        return () => { active = false; };
    }, [page]);
    async function edit(id: string, discard = false) {
        if (dirty && !discard && !window.confirm("Discard unsaved property changes?")) return;
        setBusy(true); setMessage("");
        try {
            const result = await catalogRequest<Property>(`properties/${id}`);
            if (result.data.archivedAt) { setMessage("This property was archived. Start a new property instead."); return; }
            const value = result.data; setForm({label: value.label, addressLine1: value.addressLine1, addressLine2: value.addressLine2, city: value.city, region: value.region, postalCode: value.postalCode, countryCode: value.countryCode});
            setSelected(id); setEtag(result.etag); setConflict(false); setDirty(false);
        } catch (error) { setMessage((error as Error).message); } finally { setBusy(false); }
    }
    function reset() { setForm(empty); setSelected(null); setEtag(""); setConflict(false); setDirty(false); }
    async function save(event: FormEvent) {
        event.preventDefault(); setBusy(true); setMessage("");
        try {
            await catalogRequest<Property>(selected ? `properties/${selected}` : "properties", selected ? "PUT" : "POST", form, etag);
            reset(); await refresh(page); setMessage("Property saved.");
        } catch (error) { setMessage((error as Error).message); if (error instanceof CatalogError && error.status === 412) setConflict(true); }
        finally { setBusy(false); }
    }
    async function archive() {
        if (!selected || !window.confirm("Archive this property? It will no longer be available for future booking requests.")) return;
        setBusy(true); setMessage("");
        try { await catalogRequest(`properties/${selected}/archive`, "POST", {}, etag); reset(); await refresh(page); setMessage("Property archived. Its saved record is retained."); }
        catch (error) { setMessage((error as Error).message); if (error instanceof CatalogError && error.status === 412) setConflict(true); }
        finally { setBusy(false); }
    }
    return <main><header><a className="brand" href="/">Verified Service Marketplace</a><a href="/account">My account</a></header>
        <h1>My properties</h1><p>Save your service addresses. Only you can manage them. Provider staff cannot browse your addresses. Use fictional addresses for this demo.</p>
        <p>Availability and booking are the next implementation steps; saving an address does not create a booking.</p>
        <p role="status" aria-live="polite">{message}</p>
        {ready && <><section aria-label="Saved properties"><h2>Saved addresses</h2>
            {items.length === 0 && <p>No active properties on this page.</p>}
            {items.map(item => <article key={item.id}><h3>{item.label}</h3><p>{item.addressLine1} {item.addressLine2}<br/>{item.city}, {item.region} {item.postalCode} · {item.countryCode}</p><button disabled={busy} onClick={() => edit(item.id)}>Edit {item.label}</button></article>)}
            <button disabled={busy || page === 1} onClick={() => setPage(page - 1)}>Previous page</button> <span>Page {page}</span> <button disabled={busy || page * 20 >= total} onClick={() => setPage(page + 1)}>Next page</button>
        </section><section><h2>{selected ? "Edit property" : "Add property"}</h2>
            <form onSubmit={save}><fieldset disabled={busy || conflict}>
                {fields.map(([key, label, maxLength, required]) => <label key={key}>{label}<input name={key} value={form[key]} maxLength={maxLength} required={required} autoComplete="off" onChange={event => {setForm({...form, [key]: key === "countryCode" ? event.target.value.toUpperCase() : event.target.value}); setDirty(true);}}/></label>)}
                <button type="submit">{selected ? "Save changes" : "Add property"}</button>
            </fieldset></form>
            {selected && <><button disabled={busy || conflict} onClick={archive}>Archive property</button> <button disabled={busy} onClick={() => { if (!dirty || window.confirm("Discard unsaved changes?")) reset(); }}>Start new property</button></>}
            {conflict && selected && <p>Your changes were not saved. <button disabled={busy} onClick={() => {if(window.confirm("Discard your edits and reload the latest property?")) edit(selected, true);}}>Reload latest property</button></p>}
        </section></>}
        {!ready && <p><a href="/login">Sign in</a> or <a href="/register">create an account</a>.</p>}
    </main>;
}
