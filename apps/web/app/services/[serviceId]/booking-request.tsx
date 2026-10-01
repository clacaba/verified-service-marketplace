"use client";
import {useState} from "react";
import {catalogRequest} from "../../../lib/catalog-client";
export default function BookingRequest({serviceId}: {serviceId: string}) {
    const [slots, setSlots] = useState<{startsAt: string; endsAt: string}[]>([]); const [properties, setProperties] = useState<{id: string; label: string}[]>([]); const [property, setProperty] = useState(""); const [slot, setSlot] = useState("");
    const [day, setDay] = useState(""); const [zone, setZone] = useState(""); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
    async function load() {setBusy(true); setMessage(""); setSlots([]); setSlot(""); try {
        const addresses = await catalogRequest<{items: typeof properties}>("properties?pageSize=100"); setProperties(addresses.data.items); setProperty(addresses.data.items[0]?.id ?? "");
        // The date picker is UTC; API results explicitly display the provider timezone.
        const from = day ? new Date(day + "T00:00:00Z") : new Date(Date.now() + 60000); const until = new Date(from.getTime() + 86400000);
        const result = await catalogRequest<{items: typeof slots; timeZone: string}>(`services/${serviceId}/availability?from=${encodeURIComponent(from.toISOString())}&until=${encodeURIComponent(until.toISOString())}`); setSlots(result.data.items); setZone(result.data.timeZone); setSlot(result.data.items[0]?.startsAt ?? ""); if(!result.data.items.length) setMessage("No available slots in this UTC date range.");
    } catch(error) {setMessage((error as Error).message + " Sign in first and add a property if needed.");} finally {setBusy(false);}}
    async function book() {setBusy(true); setMessage(""); try {await catalogRequest("bookings", "POST", {serviceId, propertyId: property, startsAt: slot}); setSlots([]); setSlot(""); setMessage("Booking requested. The slot is held pending provider confirmation. View My bookings for the deadline and updates.");} catch(error) {setMessage((error as Error).message + " Refresh availability before retrying."); setSlots([]); setSlot("");} finally {setBusy(false);}}
    return <section><h2>Request this service</h2><p>Verify your email and save a fictional service address first. Price and address are captured by the server when the request succeeds. No payment is collected.</p><label>UTC date (optional; defaults to the next 24 hours)<input type="date" value={day} onChange={e => {setDay(e.target.value); setSlots([]); setSlot("");}}/></label><button disabled={busy} onClick={load}>Find available times</button>
        {slots.length > 0 && <><label>Property <span className="required-marker" aria-hidden="true">*</span><select required value={property} onChange={e => setProperty(e.target.value)}>{properties.map(p => <option key={p.id} value={p.id}>{p.label}</option>)}</select></label><label>Available time ({zone}) <span className="required-marker" aria-hidden="true">*</span><select required value={slot} onChange={e => setSlot(e.target.value)}>{slots.map(s => <option key={s.startsAt} value={s.startsAt}>{new Date(s.startsAt).toLocaleString("en-US", {timeZone: zone})}</option>)}</select></label><button disabled={busy || !property || !slot} onClick={book}>Request booking</button></>}
        <p role="status">{message}</p><a href="/properties">My properties</a> <a href="/bookings">My bookings</a>
    </section>;
}
